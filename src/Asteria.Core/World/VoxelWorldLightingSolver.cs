using Asteria.Core.Content;

namespace Asteria.Core.World;

public static class VoxelWorldLightingSolver
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

    public static IReadOnlyList<WorldVoxelCoord> Initialize(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        PackContentRegistry<ObjectDefinition>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);

        var previous = CaptureLight(world);
        var objectEmissions = ObjectLightEmissionField.Capture(world, objects);

        foreach (var coord in world.LoadedChunkCoords)
        {
            world.GetChunk(coord).ClearLight();
        }

        SeedDirectLight(
            world,
            blocks,
            fluids, objectEmissions);
        Relax(
            world,
            blocks,
            fluids);

        return FindChanges(world, previous);
    }

    /// <summary>
    /// Classifies small player edits before paying for a world snapshot.
    /// A dark edit needs no solve; RGB-only changes use a local snapshot;
    /// sky-only changes skip RGB propagation; mixed/Object changes use the full solver.
    /// </summary>
    internal static LightingWorkKind ClassifyInteractiveLightingImpact(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        IReadOnlyList<WorldVoxelCoord> seedPositions)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(seedPositions);

        if (seedPositions.Count == 0)
            return LightingWorkKind.None;

        var checkedObjectChunks = new HashSet<ChunkCoord>();
        var result = LightingWorkKind.None;
        foreach (var seed in seedPositions)
        {
            var seedChunk = VoxelCoordinates.FromWorld(
                seed.X, Math.Max(seed.Y, 0), seed.Z).Chunk;
            // Object emission is stored outside block/fluid cell arrays.
            // Keep a full solve when nearby Objects might contribute light.
            for (var dy = -1; dy <= 1; dy++)
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                var coord = new ChunkCoord(
                    seedChunk.X + dx,
                    seedChunk.Y + dy,
                    seedChunk.Z + dz);
                if (coord.Y < 0 || !checkedObjectChunks.Add(coord))
                    continue;
                if (world.TryGetChunk(coord, out var objectChunk) &&
                    objectChunk.ObjectCount > 0)
                {
                    return LightingWorkKind.Full;
                }
            }

            result = MaxWorkKind(
                result, ClassifyLightAt(world, blocks, fluids, seed));
            // A missing voxel immediately above the edit may be above the
            // highest loaded chunk and acts as a virtual full-sky source.
            var above = seed + (0, 1, 0);
            if (above.Y >= 0 && !world.IsLoadedAt(above))
                result = MaxWorkKind(result, LightingWorkKind.SkyOnly);

            foreach (var offset in Neighbors)
            {
                result = MaxWorkKind(
                    result,
                    ClassifyLightAt(world, blocks, fluids, seed + offset));
                if (result == LightingWorkKind.Full)
                    return result;
            }
        }

        return result;
    }

    private static LightingWorkKind ClassifyLightAt(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        WorldVoxelCoord position)
    {
        var address = VoxelCoordinates.FromWorld(
            position.X, Math.Max(position.Y, 0), position.Z);
        if (!world.TryGetChunk(address.Chunk, out var chunk))
            return LightingWorkKind.None;

        var local = address.Local;
        var light = chunk.GetLight(local.X, local.Y, local.Z);
        var result = light.Sky > 0
            ? LightingWorkKind.SkyOnly
            : LightingWorkKind.None;
        if (light.BlockPeak > 0)
            result = MaxWorkKind(result, LightingWorkKind.BlockLightOnly);

        var cell = chunk.GetCell(local.X, local.Y, local.Z);
        var fluid = chunk.GetFluid(local.X, local.Y, local.Z);
        if (!VoxelLightingMedium.Emission(blocks, fluids, cell, fluid).IsDark)
            result = MaxWorkKind(result, LightingWorkKind.BlockLightOnly);
        return result;
    }

    private static LightingWorkKind MaxWorkKind(
        LightingWorkKind left,
        LightingWorkKind right)
    {
        if (left == LightingWorkKind.Full || right == LightingWorkKind.Full)
            return LightingWorkKind.Full;

        var hasSky = left == LightingWorkKind.SkyOnly ||
                     right == LightingWorkKind.SkyOnly;
        var hasBlock = left == LightingWorkKind.BlockLightOnly ||
                       right == LightingWorkKind.BlockLightOnly;
        if (hasSky && hasBlock)
            return LightingWorkKind.Full;
        if (hasSky)
            return LightingWorkKind.SkyOnly;
        if (hasBlock)
            return LightingWorkKind.BlockLightOnly;
        return LightingWorkKind.None;
    }

    public static VoxelLightingUpdateResult RelightAfterEdits(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        IEnumerable<WorldVoxelCoord> editedPositions,
        PackContentRegistry<ObjectDefinition>? objects = null,
        CancellationToken cancellationToken = default) =>
        RelightAfterEditsCore(
            world, blocks, fluids, editedPositions, objects,
            cancellationToken, LightingWorkKind.Full);

    internal static VoxelLightingUpdateResult RelightBlockLightAfterEdits(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        IEnumerable<WorldVoxelCoord> editedPositions,
        PackContentRegistry<ObjectDefinition>? objects = null,
        CancellationToken cancellationToken = default) =>
        RelightAfterEditsCore(
            world, blocks, fluids, editedPositions, objects,
            cancellationToken, LightingWorkKind.BlockLightOnly);

    internal static VoxelLightingUpdateResult RelightSkyOnlyAfterEdits(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        IEnumerable<WorldVoxelCoord> editedPositions,
        PackContentRegistry<ObjectDefinition>? objects = null,
        CancellationToken cancellationToken = default) =>
        RelightAfterEditsCore(
            world, blocks, fluids, editedPositions, objects,
            cancellationToken, LightingWorkKind.SkyOnly);

    private static VoxelLightingUpdateResult RelightAfterEditsCore(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        IEnumerable<WorldVoxelCoord> editedPositions,
        PackContentRegistry<ObjectDefinition>? objects,
        CancellationToken cancellationToken,
        LightingWorkKind workKind)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(editedPositions);
        if (workKind is not (
                LightingWorkKind.Full or
                LightingWorkKind.SkyOnly or
                LightingWorkKind.BlockLightOnly))
            throw new ArgumentOutOfRangeException(nameof(workKind));
        cancellationToken.ThrowIfCancellationRequested();

        // First remove stale illumination, then restore light monotonically.
        // The previous single queue reduced light one level at a time and
        // revisited the same voxel repeatedly across large neighborhoods.
        var darkening = new LightingPropagationQueue();
        var brightening = new LightingPropagationQueue();
        var originals = new Dictionary<WorldVoxelCoord, VoxelLight>();
        var objectEmissions = ObjectLightEmissionField.Capture(world, objects);
        var lightingContext = new LightingSolveContext(
            world, blocks, fluids, objectEmissions, workKind);
        foreach (var position in editedPositions)
        {
            EnqueueWithNeighbors(world, darkening, position);
            EnqueueWithNeighbors(world, brightening, position);
        }

        var processed = RemoveStaleLight(
            world, lightingContext,
            darkening, brightening, originals, cancellationToken);
        processed += RestoreLight(
            world, lightingContext,
            brightening, originals, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new VoxelLightingUpdateResult(
            originals
                .Where(entry => world.GetLightOrDark(entry.Key) != entry.Value)
                // A bounded publication slice should finish nearby meshlets
                // before moving to the next chunk. Global voxel ordering
                // repeatedly invalidated the same meshlets across slices.
                .Select(entry => (
                    Position: entry.Key,
                    Chunk: VoxelCoordinates.FromWorld(
                        entry.Key.X, entry.Key.Y, entry.Key.Z).Chunk))
                .OrderBy(entry => entry.Chunk.Y)
                .ThenBy(entry => entry.Chunk.Z)
                .ThenBy(entry => entry.Chunk.X)
                .ThenBy(entry => entry.Position.Y)
                .ThenBy(entry => entry.Position.Z)
                .ThenBy(entry => entry.Position.X)
                .Select(entry => entry.Position)
                .ToArray(),
            processed,
            lightingContext.MediumSampleRequests,
            lightingContext.MediumSampleCacheMisses);
    }

    private static int RemoveStaleLight(
        VoxelWorld world,
        LightingSolveContext lightingContext,
        LightingPropagationQueue darkening,
        LightingPropagationQueue brightening,
        Dictionary<WorldVoxelCoord, VoxelLight> originals,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        while (darkening.TryDequeue(out var position))
        {
            if ((processed & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var address = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            if (!world.TryGetChunk(address.Chunk, out var chunk))
                continue;

            var local = address.Local;
            var cell = chunk.GetCell(local.X, local.Y, local.Z);
            var fluid = chunk.GetFluid(local.X, local.Y, local.Z);
            var current = chunk.GetLight(local.X, local.Y, local.Z);
            var sample = lightingContext.Sample(
                address.Chunk, chunk, local.X, local.Y, local.Z,
                position, cell, fluid);
            var desired = DesiredLight(
                world, lightingContext, position, chunk, local, sample);
            processed++;

            if (desired.Sky >= current.Sky &&
                desired.Red >= current.Red &&
                desired.Green >= current.Green &&
                desired.Blue >= current.Blue)
                continue;

            var baseline = BaseLight(lightingContext, position, sample);
            var reduced = new VoxelLight(
                desired.Sky < current.Sky ? baseline.Sky : current.Sky,
                desired.Red < current.Red ? baseline.Red : current.Red,
                desired.Green < current.Green ? baseline.Green : current.Green,
                desired.Blue < current.Blue ? baseline.Blue : current.Blue);
            originals.TryAdd(position, current);
            chunk.SetLight(local.X, local.Y, local.Z, reduced);
            EnqueueWithNeighbors(world, darkening, position);
            EnqueueWithNeighbors(world, brightening, position);
        }
        return processed;
    }

    private static int RestoreLight(
        VoxelWorld world,
        LightingSolveContext lightingContext,
        LightingPropagationQueue brightening,
        Dictionary<WorldVoxelCoord, VoxelLight> originals,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        while (brightening.TryDequeue(out var position))
        {
            if ((processed & 255) == 0)
                cancellationToken.ThrowIfCancellationRequested();
            var address = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            if (!world.TryGetChunk(address.Chunk, out var chunk))
                continue;

            var local = address.Local;
            var cell = chunk.GetCell(local.X, local.Y, local.Z);
            var fluid = chunk.GetFluid(local.X, local.Y, local.Z);
            var current = chunk.GetLight(local.X, local.Y, local.Z);
            var sample = lightingContext.Sample(
                address.Chunk, chunk, local.X, local.Y, local.Z,
                position, cell, fluid);
            var desired = DesiredLight(
                world, lightingContext, position, chunk, local, sample);
            processed++;
            var increased = VoxelLight.Max(current, desired);
            if (increased == current)
                continue;

            originals.TryAdd(position, current);
            chunk.SetLight(local.X, local.Y, local.Z, increased);
            EnqueueWithNeighbors(world, brightening, position);
        }
        return processed;
    }

    private static VoxelLight BaseLight(
        LightingSolveContext lightingContext,
        WorldVoxelCoord position,
        LightingSample sample) =>
        new(
            !lightingContext.UsesDirectSky
                ? (byte)0
                : sample.Dampening >= VoxelLight.MaxLevel
                    ? (byte)0
                    : lightingContext.LevelAt(position),
            sample.Emission.Red,
            sample.Emission.Green,
            sample.Emission.Blue);

    private static VoxelLight DesiredLight(
        VoxelWorld world,
        LightingSolveContext lightingContext,
        WorldVoxelCoord position,
        Chunk chunk,
        LocalVoxelCoord local,
        LightingSample sample)
    {
        var dampening = sample.Dampening;
        var emission = sample.Emission;
        var current = chunk.GetLight(local.X, local.Y, local.Z);
        var updatesBlockLight = lightingContext.UpdatesBlockLight;
        var red = updatesBlockLight ? emission.Red : current.Red;
        var green = updatesBlockLight ? emission.Green : current.Green;
        var blue = updatesBlockLight ? emission.Blue : current.Blue;

        if (dampening >= VoxelLight.MaxLevel)
        {
            return new VoxelLight(
                lightingContext.UsesDirectSky ? (byte)0 : current.Sky,
                red,
                green,
                blue);
        }

        var attenuation = Math.Max((byte)1, dampening);
        var sky = lightingContext.UsesDirectSky
            ? lightingContext.LevelAt(position)
            : current.Sky;

        foreach (var (dx, dy, dz) in Neighbors)
        {
            var x = local.X + dx;
            var y = local.Y + dy;
            var z = local.Z + dz;
            var neighborLight =
                (uint)x < Chunk.Size &&
                (uint)y < Chunk.Size &&
                (uint)z < Chunk.Size
                    ? chunk.GetLight(x, y, z)
                    : world.GetLightOrDark(position + (dx, dy, dz));
            if (lightingContext.UsesDirectSky)
                sky = Math.Max(
                    sky, SaturatingSubtract(neighborLight.Sky, attenuation));
            if (updatesBlockLight)
            {
                red = Math.Max(red, SaturatingSubtract(neighborLight.Red, attenuation));
                green = Math.Max(green, SaturatingSubtract(neighborLight.Green, attenuation));
                blue = Math.Max(blue, SaturatingSubtract(neighborLight.Blue, attenuation));
            }
        }

        return new VoxelLight(sky, red, green, blue);
    }

    private static void EnqueueWithNeighbors(
        VoxelWorld world,
        LightingPropagationQueue queue,
        WorldVoxelCoord position)
    {
        EnqueueIfLoaded(world, queue, position);

        foreach (var offset in Neighbors)
        {
            EnqueueIfLoaded(world, queue, position + offset);
        }
    }

    private static void EnqueueIfLoaded(
        VoxelWorld world,
        LightingPropagationQueue queue,
        WorldVoxelCoord position)
    {
        if (world.IsLoadedAt(position))
        {
            queue.Enqueue(position);
        }
    }

    private static Dictionary<ChunkCoord, VoxelLight[]> CaptureLight(
        VoxelWorld world)
    {
        var result = new Dictionary<ChunkCoord, VoxelLight[]>();

        foreach (var coord in world.LoadedChunkCoords)
        {
            var chunk = world.GetChunk(coord);
            var values = new VoxelLight[Chunk.Volume];
            var index = 0;

            for (var y = 0; y < Chunk.Size; y++)
            {
                for (var z = 0; z < Chunk.Size; z++)
                {
                    for (var x = 0; x < Chunk.Size; x++)
                    {
                        values[index++] = chunk.GetLight(x, y, z);
                    }
                }
            }

            result.Add(coord, values);
        }

        return result;
    }

    private static IReadOnlyList<WorldVoxelCoord> FindChanges(
        VoxelWorld world,
        IReadOnlyDictionary<ChunkCoord, VoxelLight[]> previous)
    {
        var changed = new List<WorldVoxelCoord>();

        foreach (var coord in world.LoadedChunkCoords)
        {
            var chunk = world.GetChunk(coord);
            var before = previous[coord];
            var index = 0;
            var (originX, originY, originZ) =
                VoxelCoordinates.ChunkOrigin(coord);

            for (var y = 0; y < Chunk.Size; y++)
            {
                for (var z = 0; z < Chunk.Size; z++)
                {
                    for (var x = 0; x < Chunk.Size; x++)
                    {
                        if (before[index++] != chunk.GetLight(x, y, z))
                        {
                            changed.Add(
                                new WorldVoxelCoord(
                                    originX + x,
                                    originY + y,
                                    originZ + z));
                        }
                    }
                }
            }
        }

        return changed;
    }

    private static void SeedDirectLight(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        ObjectLightEmissionField objectEmissions)
    {
        var columns = world.LoadedChunkCoords
            .GroupBy(coord => (coord.X, coord.Z));

        foreach (var column in columns)
        {
            var chunks = column
                .OrderByDescending(coord => coord.Y)
                .ToArray();

            var sky = new byte[Chunk.Area];
            Array.Fill(sky, VoxelLight.MaxLevel);
            int? previousChunkY = null;

            foreach (var coord in chunks)
            {
                if (previousChunkY is not null &&
                    coord.Y != previousChunkY.Value - 1)
                {
                    Array.Fill(sky, VoxelLight.MaxLevel);
                }

                var chunk = world.GetChunk(coord);

                for (var y = Chunk.Size - 1; y >= 0; y--)
                {
                    for (var z = 0; z < Chunk.Size; z++)
                    {
                        for (var x = 0; x < Chunk.Size; x++)
                        {
                            var columnIndex = x + z * Chunk.Size;
                            var cell =
                                chunk.GetCell(
                                    x,
                                    y,
                                    z);
                            var fluid =
                                chunk.GetFluid(
                                    x,
                                    y,
                                    z);
                            var dampening =
                                VoxelLightingMedium.Dampening(
                                    chunk,
                                    blocks,
                                    fluids,
                                    x,
                                    y,
                                    z,
                                    cell,
                                    fluid);

                            sky[columnIndex] = SaturatingSubtract(
                                sky[columnIndex],
                                dampening);

                            var emission =
                                VoxelLightingMedium.Emission(
                                    blocks,
                                    fluids,
                                    cell,
                                    fluid,
                                    objectEmissions.At(new WorldVoxelCoord(
                                        coord.X * Chunk.Size + x,
                                        coord.Y * Chunk.Size + y,
                                        coord.Z * Chunk.Size + z)));

                            chunk.SetLight(
                                x,
                                y,
                                z,
                                new VoxelLight(
                                    sky[columnIndex],
                                    emission.Red,
                                    emission.Green,
                                    emission.Blue));
                        }
                    }
                }

                previousChunkY = coord.Y;
            }
        }
    }

    private static void Relax(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids)
    {
        var queue = new LightingPropagationQueue();

        foreach (var coord in world.LoadedChunkCoords)
        {
            var (originX, originY, originZ) =
                VoxelCoordinates.ChunkOrigin(coord);

            for (var y = 0; y < Chunk.Size; y++)
            {
                for (var z = 0; z < Chunk.Size; z++)
                {
                    for (var x = 0; x < Chunk.Size; x++)
                    {
                        queue.Enqueue(
                            new WorldVoxelCoord(
                                originX + x,
                                originY + y,
                                originZ + z));
                    }
                }
            }
        }

        while (queue.TryDequeue(out var position))
        {

            if (!world.TryGetCell(position, out var cell))
            {
                continue;
            }

            var address = VoxelCoordinates.FromWorld(
                position.X,
                position.Y,
                position.Z);
            var chunk =
                world.GetChunk(address.Chunk);
            var fluid =
                chunk.GetFluid(
                    address.Local.X,
                    address.Local.Y,
                    address.Local.Z);
            var dampening =
                VoxelLightingMedium.Dampening(
                    chunk,
                    blocks,
                    fluids,
                    address.Local.X,
                    address.Local.Y,
                    address.Local.Z,
                    cell,
                    fluid);

            if (dampening >= VoxelLight.MaxLevel)
            {
                continue;
            }

            var attenuation = Math.Max((byte)1, dampening);
            var current = world.GetLightOrDark(position);
            var candidate = current;

            foreach (var offset in Neighbors)
            {
                var neighbor = position + offset;
                if (!world.IsLoadedAt(neighbor))
                {
                    continue;
                }

                candidate = VoxelLight.Max(
                    candidate,
                    VoxelLight.Attenuate(
                        world.GetLightOrDark(neighbor),
                        attenuation));
            }

            if (candidate == current)
            {
                continue;
            }

            world.TrySetLight(position, candidate);

            foreach (var offset in Neighbors)
            {
                var neighbor = position + offset;
                if (world.IsLoadedAt(neighbor))
                {
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    // Solver frontiers are FIFO-only. A Queue + HashSet has the same
    // uniqueness contract without allocating a LinkedListNode for every
    // visited voxel, which matters when a light change spans thousands of cells.
    private sealed class LightingPropagationQueue
    {
        private readonly Queue<WorldVoxelCoord> _order = new();
        private readonly HashSet<WorldVoxelCoord> _queued = [];

        public bool Enqueue(WorldVoxelCoord position)
        {
            if (!_queued.Add(position))
                return false;

            _order.Enqueue(position);
            return true;
        }

        public bool TryDequeue(out WorldVoxelCoord position)
        {
            if (!_order.TryDequeue(out position))
                return false;

            _queued.Remove(position);
            return true;
        }
    }

    private static byte SaturatingSubtract(
        byte value,
        byte amount) =>
        value > amount
            ? (byte)(value - amount)
            : (byte)0;
    private readonly record struct LightingSample(
        byte Dampening,
        BlockLightEmission Emission);

    private sealed class LightingSolveContext
    {
        private readonly VoxelWorld _world;
        private readonly BlockRegistry _blocks;
        private readonly FluidRegistry _fluids;
        private readonly ObjectLightEmissionField _objectEmissions;
        private readonly LightingWorkKind _workKind;
        public bool UsesDirectSky =>
            _workKind is LightingWorkKind.Full or LightingWorkKind.SkyOnly;
        public bool UpdatesBlockLight =>
            _workKind is LightingWorkKind.Full or LightingWorkKind.BlockLightOnly;
        private readonly Dictionary<(int X, int Z), Column> _columns = [];
        // The immutable worker snapshot makes these derived cell properties
        // stable for the entire solve; the cache dies with this solve.
        private readonly Dictionary<ChunkCoord, ChunkLightingSamples> _samples = [];
        // Index loaded columns once instead of filtering all chunks for every
        // direct-sky query.
        private readonly Dictionary<(int X, int Z), ChunkCoord[]> _loadedColumns;
        private int _mediumSampleRequests;
        private int _mediumSampleCacheMisses;

        public int MediumSampleRequests => _mediumSampleRequests;
        public int MediumSampleCacheMisses => _mediumSampleCacheMisses;

        private sealed class ChunkLightingSamples
        {
            public readonly bool[] DampeningInitialized = new bool[Chunk.Volume];
            public readonly bool[] EmissionInitialized = new bool[Chunk.Volume];
            public readonly byte[] Dampening = new byte[Chunk.Volume];
            public readonly BlockLightEmission[] Emissions = new BlockLightEmission[Chunk.Volume];
        }

        public LightingSolveContext(
            VoxelWorld world,
            BlockRegistry blocks,
            FluidRegistry fluids,
            ObjectLightEmissionField objectEmissions,
            LightingWorkKind workKind)
        {
            _world = world;
            _blocks = blocks;
            _fluids = fluids;
            _objectEmissions = objectEmissions;
            _workKind = workKind;
            _loadedColumns = world.LoadedChunkCoords
                .GroupBy(coord => (coord.X, coord.Z))
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(coord => coord.Y).ToArray());
        }

        public LightingSample Sample(
            ChunkCoord coord,
            Chunk chunk,
            int x,
            int y,
            int z,
            WorldVoxelCoord position,
            VoxelCell cell,
            FluidCell fluid)
        {
            var dampening = DampeningAt(
                coord, chunk, x, y, z, cell, fluid);
            var emission = _workKind == LightingWorkKind.SkyOnly
                ? default
                : EmissionAt(
                    coord, chunk, x, y, z, position, cell, fluid);
            return new LightingSample(dampening, emission);
        }

        public byte DampeningAt(
            ChunkCoord coord,
            Chunk chunk,
            int x,
            int y,
            int z,
            VoxelCell cell,
            FluidCell fluid)
        {
            _mediumSampleRequests++;
            var cache = GetSamples(coord);
            var index = x + Chunk.Size * (z + Chunk.Size * y);
            if (!cache.DampeningInitialized[index])
            {
                cache.Dampening[index] = VoxelLightingMedium.Dampening(
                    chunk, _blocks, _fluids, x, y, z, cell, fluid);
                cache.DampeningInitialized[index] = true;
                _mediumSampleCacheMisses++;
            }

            return cache.Dampening[index];
        }

        public BlockLightEmission EmissionAt(
            ChunkCoord coord,
            Chunk chunk,
            int x,
            int y,
            int z,
            WorldVoxelCoord position,
            VoxelCell cell,
            FluidCell fluid)
        {
            _mediumSampleRequests++;
            var cache = GetSamples(coord);
            var index = x + Chunk.Size * (z + Chunk.Size * y);
            if (!cache.EmissionInitialized[index])
            {
                cache.Emissions[index] = VoxelLightingMedium.Emission(
                    _blocks, _fluids, cell, fluid, _objectEmissions.At(position));
                cache.EmissionInitialized[index] = true;
                _mediumSampleCacheMisses++;
            }

            return cache.Emissions[index];
        }

        private ChunkLightingSamples GetSamples(ChunkCoord coord)
        {
            if (_samples.TryGetValue(coord, out var cache))
                return cache;

            cache = new ChunkLightingSamples();
            _samples.Add(coord, cache);
            return cache;
        }

        public byte LevelAt(WorldVoxelCoord position)
        {
            var key = (position.X, position.Z);

            if (!_columns.TryGetValue(key, out var column))
            {
                column = BuildColumn(
                    position.X,
                    position.Z);
                _columns.Add(key, column);
            }

            return column.LevelAt(position.Y);
        }

        private Column BuildColumn(int worldX, int worldZ)
        {
            var horizontalAddress =
                VoxelCoordinates.FromWorld(
                    worldX,
                    0,
                    worldZ);
            if (!_loadedColumns.TryGetValue(
                    (horizontalAddress.Chunk.X, horizontalAddress.Chunk.Z),
                    out var matchingChunks))
            {
                return Column.Empty;
            }

            return new Column(
                this,
                worldX,
                worldZ,
                horizontalAddress.Local.X,
                horizontalAddress.Local.Z,
                matchingChunks);
        }

        // Build direct-sky samples only down to the lowest Y queried in this
        // column. Most edit relights stay near their seeds, so eagerly walking
        // every lower chunk and allocating its sample array is wasted work.
        // Chunks are stored top-down; vertical gaps reset direct sky to full.
        private sealed class Column
        {
            private readonly LightingSolveContext? _owner;
            private readonly int _worldX;
            private readonly int _worldZ;
            private readonly int _localX;
            private readonly int _localZ;
            private readonly ChunkCoord[] _chunks;
            private readonly Dictionary<int, int> _indicesByChunkY;
            private readonly Dictionary<int, byte[]> _levelsByChunk = [];
            private readonly int _highestY;
            private readonly int _lowestY;
            private int _nextChunkIndex;
            private int? _previousChunkY;
            private byte _sky = VoxelLight.MaxLevel;

            private Column()
            {
                _chunks = [];
                _indicesByChunkY = [];
                _highestY = -1;
                _lowestY = 0;
            }

            public Column(
                LightingSolveContext owner,
                int worldX,
                int worldZ,
                int localX,
                int localZ,
                ChunkCoord[] chunks)
            {
                _owner = owner;
                _worldX = worldX;
                _worldZ = worldZ;
                _localX = localX;
                _localZ = localZ;
                _chunks = chunks;
                _indicesByChunkY = new Dictionary<int, int>(chunks.Length);
                for (var index = 0; index < chunks.Length; index++)
                    _indicesByChunkY.Add(chunks[index].Y, index);

                _highestY = (chunks[0].Y + 1) * Chunk.Size - 1;
                _lowestY = chunks[^1].Y * Chunk.Size;
            }

            public static Column Empty { get; } = new();

            public byte LevelAt(int worldY)
            {
                if (worldY > _highestY)
                    return VoxelLight.MaxLevel;
                if (worldY < _lowestY)
                    return 0;

                var address = VoxelCoordinates.FromWorld(
                    _worldX, worldY, _worldZ);
                if (!_indicesByChunkY.TryGetValue(
                        address.Chunk.Y, out var targetIndex))
                {
                    return 0; // Unloaded vertical gaps remain dark.
                }

                EnsureLevelsThrough(targetIndex);
                return _levelsByChunk[address.Chunk.Y][address.Local.Y];
            }

            private void EnsureLevelsThrough(int targetIndex)
            {
                while (_nextChunkIndex <= targetIndex)
                {
                    var coord = _chunks[_nextChunkIndex];
                    if (_previousChunkY is not null &&
                        coord.Y != _previousChunkY.Value - 1)
                    {
                        _sky = VoxelLight.MaxLevel;
                    }

                    var owner = _owner ??
                        throw new InvalidOperationException(
                            "The empty direct-sky column cannot contain chunks.");
                    var chunk = owner._world.GetChunk(coord);
                    var levels = new byte[Chunk.Size];
                    for (var localY = Chunk.Size - 1; localY >= 0; localY--)
                    {
                        var cell = chunk.GetCell(_localX, localY, _localZ);
                        var fluid = chunk.GetFluid(_localX, localY, _localZ);
                        var dampening = owner.DampeningAt(
                            coord, chunk, _localX, localY, _localZ, cell, fluid);
                        _sky = SaturatingSubtract(_sky, dampening);
                        levels[localY] = _sky;
                    }

                    _levelsByChunk.Add(coord.Y, levels);
                    _previousChunkY = coord.Y;
                    _nextChunkIndex++;
                }
            }
        }
    }

}
