using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelWorldTests
{
    [Fact]
    public void WorldReadsAndWritesAcrossNegativeChunkBoundaries()
    {
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(-1, 0, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());

        var stone = new BlockRuntimeId(1);

        Assert.True(world.SetBlockAt(
            new WorldVoxelCoord(-1, 3, 4),
            stone,
            out var edit));

        Assert.Equal(new ChunkCoord(-1, 0, 0), edit.Chunk);
        Assert.Equal(new LocalVoxelCoord(15, 3, 4), edit.Local);
        Assert.Equal(
            stone,
            world.GetCellOrEmpty(
                new WorldVoxelCoord(-1, 3, 4)).Block);
    }

    [Fact]
    public void WorkerCloneKeepsIndependentChunkStateAndRevision()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var revision = world.Revision;

        var clone = world.CloneForWorker();
        clone.SetBlockAt(
            new WorldVoxelCoord(1, 1, 1),
            new BlockRuntimeId(1),
            out _);

        Assert.Equal(revision, world.Revision);
        Assert.True(world.GetCellOrEmpty(
            new WorldVoxelCoord(1, 1, 1)).IsEmpty);
    }
}
