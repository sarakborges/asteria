using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Detached intermediate state after direct sky and local emissions are
/// initialized. It remains private to one materialization task until LIGHT.
/// </summary>
public sealed class ChunkLightingInitializationState
{
    internal ChunkLightingInitializationState(
        Chunk chunk,
        byte[]? mediumDampening,
        bool needsPropagation)
    {
        Chunk = chunk ?? throw new ArgumentNullException(nameof(chunk));
        MediumDampening = mediumDampening;
        NeedsPropagation = needsPropagation;
    }

    public Chunk Chunk { get; }

    public bool NeedsPropagation { get; }

    internal byte[]? MediumDampening { get; }
}

/// <summary>Initial-light pipeline with separate INITIALIZE_LIGHT and LIGHT work.</summary>
public sealed class ChunkLightingPipeline
{
    private readonly Func<Chunk, ChunkLightingInitializationState> _initializeSources;
    private readonly Func<ChunkLightingInitializationState, int> _propagate;

    public ChunkLightingPipeline(
        Func<Chunk, ChunkLightingInitializationState> initializeSources,
        Func<ChunkLightingInitializationState, int> propagate)
    {
        _initializeSources = initializeSources ??
            throw new ArgumentNullException(nameof(initializeSources));
        _propagate = propagate ??
            throw new ArgumentNullException(nameof(propagate));
    }

    public ChunkLightingInitializationState InitializeSources(Chunk chunk) =>
        _initializeSources(chunk);

    public int Propagate(ChunkLightingInitializationState state) =>
        _propagate(state);

    /// <summary>
    /// Compatibility adapter for older combined lighting callbacks. Runtime
    /// world generation should use ChunkLightingSolver.CreatePipeline.
    /// </summary>
    public static ChunkLightingPipeline FromCombinedInitializer(
        Action<Chunk> initialize)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        return new ChunkLightingPipeline(
            chunk =>
            {
                initialize(chunk);
                return new ChunkLightingInitializationState(
                    chunk, mediumDampening: null, needsPropagation: false);
            },
            _ => 0);
    }
}

public static class ChunkLightingSolver
{
    private static readonly (int X, int Y, int Z)[] Neighbors =
    [
        (1, 0, 0),
        (-1, 0, 0),
        (0, 1, 0),
        (0, -1, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];

    public static void Initialize(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids,
        PackContentRegistry<ObjectDefinition>? objects = null)
    {
        var initialized = InitializeSources(chunk, blocks, fluids, objects);
        Propagate(initialized);
    }

    /// <summary>Seeds direct sky and local emission without neighbor propagation.</summary>
    internal static ChunkLightingInitializationState InitializeSources(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids,
        PackContentRegistry<ObjectDefinition>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);

        if (chunk.IsEmpty && chunk.ObjectCount == 0)
        {
            chunk.FillLight(new VoxelLight(VoxelLight.MaxLevel, 0, 0, 0));
            return new ChunkLightingInitializationState(
                chunk, mediumDampening: null, needsPropagation: false);
        }

        // Uniform fluid layers can solve direct sky without retaining a
        // propagation snapshot.
        if (TryInitializeUniformFluidLayers(chunk, fluids))
            return new ChunkLightingInitializationState(
                chunk, mediumDampening: null, needsPropagation: false);

        var objectEmissions = ObjectLightEmissionField.Capture(chunk, objects);
        // SeedDirectLight writes every cell's complete VoxelLight value.
        // Clearing the full array first only added a redundant memory pass.
        var mediumDampening = new byte[Chunk.Volume];
        var needsRelaxation = SeedDirectLight(
            chunk, blocks, fluids, objectEmissions, mediumDampening);

        if (chunk.IsEmpty || !needsRelaxation)
        {
            return new ChunkLightingInitializationState(
                chunk, mediumDampening: null, needsPropagation: false);
        }

        return new ChunkLightingInitializationState(
            chunk, mediumDampening, needsPropagation: true);
    }

    /// <summary>Runs neighbor relaxation for the distinct LIGHT stage.</summary>
    internal static int Propagate(ChunkLightingInitializationState initialized)
    {
        ArgumentNullException.ThrowIfNull(initialized);
        if (!initialized.NeedsPropagation)
            return 0;

        var mediumDampening = initialized.MediumDampening ??
            throw new InvalidOperationException(
                "Lighting propagation state is missing its medium-dampening snapshot.");
        return Relax(initialized.Chunk, mediumDampening);
    }

    internal static int InitializeAndCountProcessedVoxels(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids,
        PackContentRegistry<ObjectDefinition>? objects = null)
    {
        var initialized = InitializeSources(chunk, blocks, fluids, objects);
        return Propagate(initialized);
    }

    public static ChunkLightingPipeline CreatePipeline(
        BlockRegistry blocks,
        FluidRegistry fluids,
        PackContentRegistry<ObjectDefinition>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        return new ChunkLightingPipeline(
            chunk => InitializeSources(chunk, blocks, fluids, objects),
            Propagate);
    }

    internal static bool TryInitializeUniformFluidLayers(
        Chunk chunk,
        FluidRegistry fluids)
    {
        if (chunk.NonEmptyVoxelCount != 0 ||
            chunk.ObjectCount != 0 ||
            chunk.FluidCount == 0)
        {
            return false;
        }

        var counts = new int[Chunk.Size];
        var dampeningByY = new byte[Chunk.Size];
        var initializedY = new bool[Chunk.Size];
        var compatible = true;

        chunk.VisitFluidCells((_, y, _, fluid) =>
        {
            if (!compatible)
                return;

            var emission = fluids.GetLightEmissionAtLevel(
                fluid.Fluid, fluid.Level);
            if (!emission.IsDark)
            {
                compatible = false;
                return;
            }

            var dampening = fluids.GetLightDampeningAtLevel(
                fluid.Fluid, fluid.Level);
            if (initializedY[y] && dampeningByY[y] != dampening)
            {
                compatible = false;
                return;
            }

            initializedY[y] = true;
            dampeningByY[y] = dampening;
            counts[y]++;
        });

        if (!compatible)
            return false;

        for (var y = 0; y < Chunk.Size; y++)
        {
            if (counts[y] != 0 && counts[y] != Chunk.Area)
                return false;

            if (counts[y] == 0)
                dampeningByY[y] = 0;
        }

        byte sky = VoxelLight.MaxLevel;
        for (var y = Chunk.Size - 1; y >= 0; y--)
        {
            sky = SaturatingSubtract(sky, dampeningByY[y]);
            chunk.FillLightLayer(y, new VoxelLight(sky, 0, 0, 0));
        }

        return true;
    }

    private static bool SeedDirectLight(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids,
        ObjectLightEmissionField objectEmissions,
        byte[] mediumDampening)
    {
        // Only attenuated transmitting cells or emissive cells can
        // increase another voxel's seeded light. A horizontally uniform
        // direct-sky profile is already stable without a local flood-fill.
        var needsPropagation = false;
        var hasEmission = false;
        var hasObjectEmissions = !objectEmissions.IsEmpty;
        var uniformDirectSkyAcrossColumns = true;
        var referenceDirectSky = new byte[Chunk.Size];
        var isFirstColumn = true;

        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                byte sky =
                    VoxelLight.MaxLevel;

                for (var y =
                         Chunk.Size - 1;
                     y >= 0;
                     y--)
                {
                    var cell =
                        chunk.GetCell(x, y, z);
                    var fluid =
                        chunk.GetFluid(x, y, z);
                    var dampening = cell.IsEmpty && fluid.IsEmpty
                        ? (byte)0
                        : VoxelLightingMedium.Dampening(
                            chunk, blocks, fluids, x, y, z, cell, fluid);

                    mediumDampening[ToIndex(x, y, z)] = dampening;

                    if (sky > 0)
                    {
                        sky =
                            SaturatingSubtract(
                                sky,
                                dampening);
                    }

                    if (isFirstColumn)
                    {
                        referenceDirectSky[y] = sky;
                    }
                    else if (sky != referenceDirectSky[y])
                    {
                        uniformDirectSkyAcrossColumns = false;
                    }

                    var emission = cell.IsEmpty && fluid.IsEmpty &&
                                   !hasObjectEmissions
                        ? default
                        : VoxelLightingMedium.Emission(
                            blocks, fluids, cell, fluid,
                            hasObjectEmissions
                                ? objectEmissions.At(new WorldVoxelCoord(x, y, z))
                                : default);
                    hasEmission |= !emission.IsDark;
                    needsPropagation |= !emission.IsDark ||
                        (dampening < VoxelLight.MaxLevel &&
                         sky < VoxelLight.MaxLevel);

                    chunk.SetLight(
                        x,
                        y,
                        z,
                        new VoxelLight(
                            sky,
                            emission.Red,
                            emission.Green,
                            emission.Blue));
                }
                isFirstColumn = false;
            }
        }

        // Symmetric terrain/water layers are already solved by direct sky.
        // Relax only when some column has a different direct-sky profile, or
        // when an emissive source can illuminate neighboring cells.
        return needsPropagation &&
               (!uniformDirectSkyAcrossColumns || hasEmission);
    }

    private static int Relax(
        Chunk chunk,
        byte[] mediumDampening)
    {
        // Most fluid voxels already have their final direct-sky value. Only
        // seed cells where an existing neighbor can actually improve light;
        // otherwise a whole ocean-filled chunk would enqueue every water cell.
        var queue = new Queue<int>();
        var queued = new bool[Chunk.Volume];

        for (var y = 0; y < Chunk.Size; y++)
        {
            for (var z = 0; z < Chunk.Size; z++)
            {
                for (var x = 0; x < Chunk.Size; x++)
                {
                    var index = ToIndex(x, y, z);
                    var dampening = mediumDampening[index];
                    if (dampening >= VoxelLight.MaxLevel)
                        continue;

                    var current = chunk.GetLight(x, y, z);
                    var attenuation = Math.Max((byte)1, dampening);
                    if (CanReceiveMoreLight(
                            chunk, x, y, z, current, attenuation))
                    {
                        Enqueue(queue, queued, x, y, z);
                    }
                }
            }
        }

        var processed = 0;
        while (queue.Count > 0)
        {
            var index = queue.Dequeue();
            queued[index] = false;
            var (x, y, z) = FromIndex(index);
            var dampening = mediumDampening[index];
            var attenuation = Math.Max((byte)1, dampening);
            var current = chunk.GetLight(x, y, z);

            // Direct-sky seeding already includes each cell's block, fluid and
            // object emission. Re-querying registries here was redundant and
            // especially costly for fluid-heavy chunks.
            var candidate = current;
            foreach (var (dx, dy, dz) in Neighbors)
            {
                var nx = x + dx;
                var ny = y + dy;
                var nz = z + dz;
                if (!Chunk.Contains(nx, ny, nz))
                    continue;

                candidate = VoxelLight.Max(
                    candidate,
                    VoxelLight.Attenuate(
                        chunk.GetLight(nx, ny, nz),
                        attenuation));
            }

            processed++;
            if (candidate == current)
                continue;

            chunk.SetLight(x, y, z, candidate);
            foreach (var (dx, dy, dz) in Neighbors)
            {
                var nx = x + dx;
                var ny = y + dy;
                var nz = z + dz;
                if (Chunk.Contains(nx, ny, nz) &&
                    mediumDampening[ToIndex(nx, ny, nz)] < VoxelLight.MaxLevel)
                {
                    Enqueue(queue, queued, nx, ny, nz);
                }
            }
        }

        return processed;
    }

    private static bool CanReceiveMoreLight(
        Chunk chunk,
        int x,
        int y,
        int z,
        VoxelLight current,
        byte attenuation)
    {
        foreach (var (dx, dy, dz) in Neighbors)
        {
            var nx = x + dx;
            var ny = y + dy;
            var nz = z + dz;
            if (!Chunk.Contains(nx, ny, nz))
                continue;

            var propagated = VoxelLight.Attenuate(
                chunk.GetLight(nx, ny, nz),
                attenuation);
            if (VoxelLight.Max(current, propagated) != current)
                return true;
        }

        return false;
    }

    public static byte MediumDampening(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids,
        int x,
        int y,
        int z) =>
        VoxelLightingMedium.Dampening(
            chunk,
            blocks,
            fluids,
            x,
            y,
            z);

    private static void Enqueue(
        Queue<int> queue,
        bool[] queued,
        int x,
        int y,
        int z)
    {
        var index =
            ToIndex(x, y, z);

        if (queued[index])
        {
            return;
        }

        queued[index] = true;
        queue.Enqueue(index);
    }

    private static int ToIndex(
        int x,
        int y,
        int z) =>
        x +
        Chunk.Size *
        (z + Chunk.Size * y);

    private static (
        int X,
        int Y,
        int Z) FromIndex(
        int index)
    {
        var y =
            index / Chunk.Area;
        var remainder =
            index - y * Chunk.Area;
        var z =
            remainder / Chunk.Size;
        var x =
            remainder -
            z * Chunk.Size;
        return (x, y, z);
    }

    private static byte SaturatingSubtract(
        byte value,
        byte amount) =>
        value > amount
            ? (byte)(value - amount)
            : (byte)0;
}
