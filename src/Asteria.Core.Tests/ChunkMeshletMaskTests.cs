using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkMeshletMaskTests
{
    [Fact]
    public void InteriorEditTouchesOnlyItsMeshlet()
    {
        var mask = ChunkMeshletMask.ForWorldPosition(
            new ChunkCoord(0, 0, 0),
            new WorldVoxelCoord(3, 3, 3));

        Assert.Equal(1, mask.SelectedCount);
        Assert.True(mask.ContainsIndex(0));
    }

    [Fact]
    public void EditOnInternalMeshletBoundaryTouchesBothSides()
    {
        var mask = ChunkMeshletMask.ForWorldPosition(
            new ChunkCoord(0, 0, 0),
            new WorldVoxelCoord(7, 3, 3));

        Assert.Equal(2, mask.SelectedCount);
        Assert.True(mask.ContainsIndex(0));
        Assert.True(mask.ContainsIndex(1));
    }

    [Fact]
    public void WorldChunkBoundaryIncludesNeighborHalo()
    {
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 0, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        var queue = new WorldUpdateQueue();

        queue.EnqueueVoxelEdit(
            world,
            new WorldVoxelCoord(15, 2, 2));

        var batch = queue.Drain();

        Assert.Contains(
            new ChunkCoord(0, 0, 0),
            batch.DirtyMeshlets.Keys);
        Assert.Contains(
            new ChunkCoord(1, 0, 0),
            batch.DirtyMeshlets.Keys);
    }
}
