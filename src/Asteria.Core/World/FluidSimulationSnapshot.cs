namespace Asteria.Core.World;

public sealed class FluidSimulationDependencies
{
    private readonly ChunkContentStamp _content;
    private readonly ChunkResidencyStamp _residency;

    internal FluidSimulationDependencies(
        ChunkContentStamp content,
        ChunkResidencyStamp residency)
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
               world.IsResidencyStampCurrent(
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
                source.CaptureResidencyStamp(
                    neighborhood.Chunks));

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

        if (seedChunks.Count == 0)
        {
            return new FluidSimulationNeighborhood(
                chunks);
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
                    for (var y = -VerticalChunkRadius;
                         y <= VerticalChunkRadius;
                         y++)
                    {
                        var chunkY =
                            seed.Y + y;

                        if (chunkY < 0)
                        {
                            continue;
                        }

                        chunks.Add(
                            new ChunkCoord(
                                seed.X + x,
                                chunkY,
                                seed.Z + z));
                    }
                }
            }
        }

        return new FluidSimulationNeighborhood(
            chunks);
    }
}

internal sealed record FluidSimulationNeighborhood(
    HashSet<ChunkCoord> Chunks);
