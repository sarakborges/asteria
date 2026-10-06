namespace Asteria.Core.World;

public sealed class FluidSimulationDependencies
{
    private readonly ChunkContentStamp _content;
    private readonly ChunkColumnResidencyStamp _residency;

    internal FluidSimulationDependencies(
        ChunkContentStamp content,
        ChunkColumnResidencyStamp residency)
    {
        _content =
            content ??
            throw new ArgumentNullException(nameof(content));
        _residency =
            residency ??
            throw new ArgumentNullException(nameof(residency));
    }

    public bool IsCurrent(VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);

        return world.IsContentStampCurrent(_content) &&
               world.IsColumnResidencyStampCurrent(
                   _residency);
    }
}

public sealed class FluidSimulationSnapshot
{
    private const int VerticalChunkRadius = 1;

    private FluidSimulationSnapshot(
        VoxelWorld world,
        FluidSimulationDependencies dependencies)
    {
        World = world;
        Dependencies = dependencies;
    }

    public VoxelWorld World { get; }

    public FluidSimulationDependencies Dependencies { get; }

    public static FluidSimulationSnapshot Capture(
        VoxelWorld source,
        FluidWorkBatch batch,
        int horizontalVoxelRadius)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(batch);

        var neighborhood =
            DescribeNeighborhood(
                batch.Positions,
                horizontalVoxelRadius);

        if (neighborhood.Chunks.Count == 0)
        {
            throw new ArgumentException(
                "A fluid simulation snapshot requires at least one valid seed position.",
                nameof(batch));
        }

        var world =
            source.CloneForWorker(
                neighborhood.Chunks);
        var dependencies =
            new FluidSimulationDependencies(
                source.CaptureContentStamp(
                    world.LoadedChunkCoords),
                source.CaptureColumnResidencyStamp(
                    neighborhood.Columns));

        return new FluidSimulationSnapshot(
            world,
            dependencies);
    }

    internal static FluidSimulationNeighborhood
        DescribeNeighborhood(
            IEnumerable<WorldVoxelCoord> seeds,
            int horizontalVoxelRadius)
    {
        ArgumentNullException.ThrowIfNull(seeds);

        if (horizontalVoxelRadius < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horizontalVoxelRadius));
        }

        var seedChunks =
            new HashSet<ChunkCoord>();

        foreach (var position in seeds)
        {
            if (position.Y < 0)
            {
                continue;
            }

            seedChunks.Add(
                VoxelCoordinates.FromWorld(
                    position.X,
                    position.Y,
                    position.Z).Chunk);
        }

        var chunks =
            new HashSet<ChunkCoord>();
        var columns =
            new HashSet<ChunkColumnCoord>();

        if (seedChunks.Count == 0)
        {
            return new FluidSimulationNeighborhood(
                chunks,
                columns);
        }

        var horizontalChunkRadius =
            horizontalVoxelRadius == 0
                ? 0
                : (
                    horizontalVoxelRadius +
                    Chunk.Size -
                    1) /
                  Chunk.Size;

        foreach (var seed in seedChunks)
        {
            for (var z = -horizontalChunkRadius;
                 z <= horizontalChunkRadius;
                 z++)
            {
                for (var x = -horizontalChunkRadius;
                     x <= horizontalChunkRadius;
                     x++)
                {
                    var column =
                        new ChunkColumnCoord(
                            seed.X + x,
                            seed.Z + z);

                    columns.Add(column);

                    for (var y = -VerticalChunkRadius;
                         y <= VerticalChunkRadius;
                         y++)
                    {
                        chunks.Add(
                            new ChunkCoord(
                                column.X,
                                seed.Y + y,
                                column.Z));
                    }
                }
            }
        }

        return new FluidSimulationNeighborhood(
            chunks,
            columns);
    }
}

internal sealed record FluidSimulationNeighborhood(
    HashSet<ChunkCoord> Chunks,
    HashSet<ChunkColumnCoord> Columns);
