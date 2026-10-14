using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class MeshletDependencyGranularityTests
{
    [Fact]
    public void ChangesToOneBatchChunkDoNotDiscardUnrelatedMeshlets()
    {
        var world = new VoxelWorld();
        var first = ChunkCoord.Zero;
        var second = new ChunkCoord(4, 0, 0);
        world.InsertChunk(first, new Chunk());
        world.InsertChunk(second, new Chunk());

        var captured = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [first] = ChunkMeshletMask.Single(0),
                [second] = ChunkMeshletMask.Single(0),
            });
        var firstKey = new ChunkMeshletKey(first, 0);
        var secondKey = new ChunkMeshletKey(second, 0);

        Assert.True(captured.Dependencies.IsCurrent(world));
        Assert.True(captured.MeshletDependencies[firstKey].IsCurrent(world));
        Assert.True(captured.MeshletDependencies[secondKey].IsCurrent(world));

        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        world.GetChunk(second).SetBlock(1, 1, 1, blocks.GetId("asteria:stone"));

        Assert.False(captured.Dependencies.IsCurrent(world));
        Assert.True(captured.MeshletDependencies[firstKey].IsCurrent(world));
        Assert.False(captured.MeshletDependencies[secondKey].IsCurrent(world));
    }

    [Fact]
    public void ArrivingDependencyInvalidatesOnlyTouchingMeshlet()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());

        var captured = world.CaptureMeshWorkerSnapshot(
            new Dictionary<ChunkCoord, ChunkMeshletMask>
            {
                [ChunkCoord.Zero] = new ChunkMeshletMask(0b00000011),
            });
        var negativeSide = new ChunkMeshletKey(ChunkCoord.Zero, 0);
        var positiveSide = new ChunkMeshletKey(ChunkCoord.Zero, 1);
        var eastern = new ChunkCoord(1, 0, 0);

        world.InsertChunk(eastern, new Chunk());

        Assert.False(captured.Dependencies.IsCurrent(world));
        Assert.True(captured.MeshletDependencies[negativeSide].IsCurrent(world));
        Assert.False(captured.MeshletDependencies[positiveSide].IsCurrent(world));
    }
}
