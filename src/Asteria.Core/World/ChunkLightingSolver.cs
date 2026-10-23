using Asteria.Core.Content;

namespace Asteria.Core.World;

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
        InitializeCore(chunk, blocks, fluids, objects);
    }

    // Exposes real propagation work to core regression tests without making
    // solver internals part of the runtime API.
    internal static int InitializeAndCountProcessedVoxels(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids,
        PackContentRegistry<ObjectDefinition>? objects = null) =>
        InitializeCore(chunk, blocks, fluids, objects);

    private static int InitializeCore(
        Chunk chunk,
        BlockRegistry blocks,
        FluidRegistry fluids,
        PackContentRegistry<ObjectDefinition>? objects)
    {
        ArgumentNullException.ThrowIfNull(chunk);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);

        // Air-only chunks need no ray march or local relaxation. An
        // attached object can still be emissive, so exclude that case.
        if (chunk.IsEmpty && chunk.ObjectCount == 0)
        {
            chunk.FillLight(new VoxelLight(VoxelLight.MaxLevel, 0, 0, 0));
            return 0;
        }

        // Ocean water often occupies complete horizontal layers above the
        // seabed. When there are no blocks, attached objects or emitters and
        // each Y layer is either all air or uniformly attenuating fluid, its
        // direct-sky field can be computed once per layer instead of sampling
        // all 4096 cells through the medium registry.
        if (TryInitializeUniformFluidLayers(chunk, fluids))
            return 0;

        var objectEmissions = ObjectLightEmissionField.Capture(chunk, objects);
        // SeedDirectLight writes every cell's complete VoxelLight value.
        // Clearing the full array first only added a redundant memory pass.
        var mediumDampening = new byte[Chunk.Volume];
        var needsRelaxation = SeedDirectLight(
            chunk, blocks, fluids, objectEmissions, mediumDampening);

        if (chunk.IsEmpty || !needsRelaxation)
            return 0;

        return Relax(chunk, mediumDampening);
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
            var light = new VoxelLight(sky, 0, 0, 0);
            for (var z = 0; z < Chunk.Size; z++)
            for (var x = 0; x < Chunk.Size; x++)
                chunk.SetLight(x, y, z, light);
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
