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

    public static VoxelLightingUpdateResult RelightAfterEdits(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        IEnumerable<WorldVoxelCoord> editedPositions,
        PackContentRegistry<ObjectDefinition>? objects = null)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(fluids);
        ArgumentNullException.ThrowIfNull(editedPositions);

        // First remove stale illumination, then restore light monotonically.
        // The previous single queue reduced light one level at a time and
        // revisited the same voxel repeatedly across large neighborhoods.
        var darkening = new DeduplicatedQueue<WorldVoxelCoord>();
        var brightening = new DeduplicatedQueue<WorldVoxelCoord>();
        var originals = new Dictionary<WorldVoxelCoord, VoxelLight>();
        var directSky = new DirectSkyContext(world, blocks, fluids);
        var objectEmissions = ObjectLightEmissionField.Capture(world, objects);
        foreach (var position in editedPositions)
        {
            EnqueueWithNeighbors(world, darkening, position);
            EnqueueWithNeighbors(world, brightening, position);
        }

        var processed = RemoveStaleLight(
            world, blocks, fluids, directSky, objectEmissions,
            darkening, brightening, originals);
        processed += RestoreLight(
            world, blocks, fluids, directSky, objectEmissions,
            brightening, originals);
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
            processed);
    }

    private static int RemoveStaleLight(
        VoxelWorld world, BlockRegistry blocks, FluidRegistry fluids,
        DirectSkyContext directSky,
        ObjectLightEmissionField objectEmissions,
        DeduplicatedQueue<WorldVoxelCoord> darkening,
        DeduplicatedQueue<WorldVoxelCoord> brightening,
        Dictionary<WorldVoxelCoord, VoxelLight> originals)
    {
        var processed = 0;
        while (darkening.TryDequeue(out var position))
        {
            var address = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            if (!world.TryGetChunk(address.Chunk, out var chunk))
                continue;

            var local = address.Local;
            var cell = chunk.GetCell(local.X, local.Y, local.Z);
            var fluid = chunk.GetFluid(local.X, local.Y, local.Z);
            var current = chunk.GetLight(local.X, local.Y, local.Z);
            var desired = DesiredLight(
                world, blocks, fluids, directSky, objectEmissions,
                position, chunk, local, cell, fluid);
            processed++;

            if (desired.Sky >= current.Sky &&
                desired.Red >= current.Red &&
                desired.Green >= current.Green &&
                desired.Blue >= current.Blue)
                continue;

            var baseline = BaseLight(
                blocks, fluids, directSky, objectEmissions,
                position, chunk, local, cell, fluid);
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
        VoxelWorld world, BlockRegistry blocks, FluidRegistry fluids,
        DirectSkyContext directSky,
        ObjectLightEmissionField objectEmissions,
        DeduplicatedQueue<WorldVoxelCoord> brightening,
        Dictionary<WorldVoxelCoord, VoxelLight> originals)
    {
        var processed = 0;
        while (brightening.TryDequeue(out var position))
        {
            var address = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z);
            if (!world.TryGetChunk(address.Chunk, out var chunk))
                continue;

            var local = address.Local;
            var cell = chunk.GetCell(local.X, local.Y, local.Z);
            var fluid = chunk.GetFluid(local.X, local.Y, local.Z);
            var current = chunk.GetLight(local.X, local.Y, local.Z);
            var desired = DesiredLight(
                world, blocks, fluids, directSky, objectEmissions,
                position, chunk, local, cell, fluid);
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
        BlockRegistry blocks, FluidRegistry fluids, DirectSkyContext directSky,
        ObjectLightEmissionField objectEmissions,
        WorldVoxelCoord position, Chunk chunk, LocalVoxelCoord local,
        VoxelCell cell, FluidCell fluid)
    {
        var dampening = VoxelLightingMedium.Dampening(
            chunk, blocks, fluids, local.X, local.Y, local.Z, cell, fluid);
        var emission = VoxelLightingMedium.Emission(
            blocks, fluids, cell, fluid, objectEmissions.At(position));
        return new VoxelLight(
            dampening >= VoxelLight.MaxLevel
                ? (byte)0
                : directSky.LevelAt(position),
            emission.Red, emission.Green, emission.Blue);
    }

    private static VoxelLight DesiredLight(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        DirectSkyContext directSky,
        ObjectLightEmissionField objectEmissions,
        WorldVoxelCoord position,
        Chunk chunk,
        LocalVoxelCoord local,
        VoxelCell cell,
        FluidCell fluid)
    {
        var dampening =
            VoxelLightingMedium.Dampening(
                chunk,
                blocks,
                fluids,
                local.X,
                local.Y,
                local.Z,
                cell,
                fluid);

        var emission =
            VoxelLightingMedium.Emission(
                blocks,
                fluids,
                cell,
                fluid,
                objectEmissions.At(position));

        if (dampening >= VoxelLight.MaxLevel)
        {
            return new VoxelLight(
                0,
                emission.Red,
                emission.Green,
                emission.Blue);
        }

        var attenuation = Math.Max((byte)1, dampening);
        var sky = directSky.LevelAt(position);
        var red = emission.Red;
        var green = emission.Green;
        var blue = emission.Blue;

        foreach (var (dx, dy, dz) in Neighbors)
        {
            var x = local.X + dx;
            var y = local.Y + dy;
            var z = local.Z + dz;
            // Most propagation steps remain inside the current chunk.
            // Absent neighboring chunks contribute darkness, exactly as
            // the previous loaded-neighbor check did.
            var neighborLight =
                (uint)x < Chunk.Size &&
                (uint)y < Chunk.Size &&
                (uint)z < Chunk.Size
                    ? chunk.GetLight(x, y, z)
                    : world.GetLightOrDark(position + (dx, dy, dz));
            sky = Math.Max(
                sky,
                SaturatingSubtract(
                    neighborLight.Sky,
                    attenuation));
            red = Math.Max(
                red,
                SaturatingSubtract(
                    neighborLight.Red,
                    attenuation));
            green = Math.Max(
                green,
                SaturatingSubtract(
                    neighborLight.Green,
                    attenuation));
            blue = Math.Max(
                blue,
                SaturatingSubtract(
                    neighborLight.Blue,
                    attenuation));
        }

        return new VoxelLight(sky, red, green, blue);
    }

    private static void EnqueueWithNeighbors(
        VoxelWorld world,
        DeduplicatedQueue<WorldVoxelCoord> queue,
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
        DeduplicatedQueue<WorldVoxelCoord> queue,
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
        var queue = new DeduplicatedQueue<WorldVoxelCoord>();

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

    private static byte SaturatingSubtract(
        byte value,
        byte amount) =>
        value > amount
            ? (byte)(value - amount)
            : (byte)0;
    private sealed class DirectSkyContext
    {
        private readonly VoxelWorld _world;
        private readonly BlockRegistry _blocks;
        private readonly FluidRegistry _fluids;
        private readonly Dictionary<(int X, int Z), Column> _columns = [];
        // Index the snapshot once. A relight can sample hundreds of distinct
        // voxel columns; filtering all resident chunks for each is quadratic.
        private readonly Dictionary<(int X, int Z), ChunkCoord[]>
            _loadedColumns;

        public DirectSkyContext(
            VoxelWorld world,
            BlockRegistry blocks,
            FluidRegistry fluids)
        {
            _world = world;
            _blocks = blocks;
            _fluids = fluids;
            _loadedColumns = world.LoadedChunkCoords
                .GroupBy(coord => (coord.X, coord.Z))
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(coord => coord.Y).ToArray());
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

            var highestY =
                (matchingChunks[0].Y + 1) * Chunk.Size - 1;
            var lowestY =
                matchingChunks[^1].Y * Chunk.Size;
            var levels =
                new byte[
                    highestY -
                    lowestY +
                    1];
            int? previousChunkY = null;
            var sky =
                VoxelLight.MaxLevel;

            foreach (var coord in matchingChunks)
            {
                if (previousChunkY is not null &&
                    coord.Y !=
                        previousChunkY.Value - 1)
                {
                    sky =
                        VoxelLight.MaxLevel;
                }

                var chunk =
                    _world.GetChunk(coord);
                var localX =
                    horizontalAddress.Local.X;
                var localZ =
                    horizontalAddress.Local.Z;
                var originY =
                    coord.Y * Chunk.Size;

                for (var localY =
                         Chunk.Size - 1;
                     localY >= 0;
                     localY--)
                {
                    var cell =
                        chunk.GetCell(
                            localX,
                            localY,
                            localZ);
                    var fluid =
                        chunk.GetFluid(
                            localX,
                            localY,
                            localZ);

                    sky = SaturatingSubtract(
                        sky,
                        VoxelLightingMedium.Dampening(
                            chunk,
                            _blocks,
                            _fluids,
                            localX,
                            localY,
                            localZ,
                            cell,
                            fluid));

                    levels[
                        originY +
                        localY -
                        lowestY] = sky;
                }

                previousChunkY =
                    coord.Y;
            }

            return new Column(
                lowestY,
                highestY,
                levels);
        }

        private sealed record Column(
            int LowestY,
            int HighestY,
            byte[] Levels)
        {
            public static Column Empty { get; } =
                new(0, -1, []);

            public byte LevelAt(int worldY)
            {
                if (worldY > HighestY)
                {
                    return VoxelLight.MaxLevel;
                }

                if (worldY < LowestY ||
                    Levels.Length == 0)
                {
                    return 0;
                }

                return Levels[worldY - LowestY];
            }
        }
    }

}
