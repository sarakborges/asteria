using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class LightingPublicationPlanTests
{
    [Fact]
    public void GroupedChunksCommitCompleteLightingWithDeferredMeshRefresh()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        var lower = new Chunk();
        var upper = new Chunk();
        lower.SetBlock(7, 4, 4, blocks.GetId("asteria:stone"));
        upper.SetBlock(2, 2, 2, blocks.GetId("asteria:stone"));
        world.InsertChunk(ChunkCoord.Zero, lower);
        world.InsertChunk(new ChunkCoord(0, 1, 0), upper);
        var snapshot = world.CloneForWorker();
        var bottom = new WorldVoxelCoord(7, 4, 4);
        var top = new WorldVoxelCoord(2, 18, 2);
        snapshot.TrySetLight(bottom, new VoxelLight(0, 12, 0, 0));
        snapshot.TrySetLight(top, new VoxelLight(0, 0, 8, 0));
        var plan = LightingPublicationPlan.Create([bottom, top]);
        Assert.Equal(2, plan.Count);
        Assert.Equal(ChunkCoord.Zero, plan[0].Coord);
        Assert.Equal(new ChunkCoord(0, 1, 0), plan[1].Coord);
        var updates = new WorldUpdateQueue();
        var integrator = new LightingResultIntegrator(
            world, updates, new FluidMeshUpdateQueue());

        integrator.AccumulatePreparedChunk(snapshot, plan[0]);
        Assert.Equal((byte)12, world.GetLightOrDark(bottom).Red);
        Assert.Equal((byte)0, world.GetLightOrDark(top).Green);
        Assert.False(updates.HasMeshWork);
        var dirty = integrator.FlushAccumulated();
        Assert.Equal(1, dirty.DirtyChunkCount);
        Assert.True(updates.HasMeshWork);

        integrator.AccumulatePreparedChunk(snapshot, plan[1]);
        Assert.Equal((byte)8, world.GetLightOrDark(top).Green);
        Assert.Equal(1, integrator.FlushAccumulated().DirtyChunkCount);
    }

    [Fact]
    public void WorkerHaloIncludesNeighborMissingDuringSnapshot()
    {
        var changed = new WorldVoxelCoord(15, 4, 4);
        var plan = LightingPublicationPlan.Create([changed]);
        var neighbor = new ChunkCoord(1, 0, 0);
        Assert.Contains(neighbor, plan[0].DirtyMeshlets.Keys);
        var world = new VoxelWorld();
        var snapshot = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        snapshot.InsertChunk(ChunkCoord.Zero, new Chunk());
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var neighborChunk = new Chunk();
        neighborChunk.SetBlock(0, 4, 4, blocks.GetId("asteria:stone"));
        world.InsertChunk(neighbor, neighborChunk);
        var updates = new WorldUpdateQueue();
        var integrator = new LightingResultIntegrator(
            world, updates, new FluidMeshUpdateQueue());
        integrator.AccumulatePreparedChunk(snapshot, plan[0]);
        Assert.Equal(1, integrator.FlushAccumulated().DirtyChunkCount);
        Assert.Contains(neighbor, updates.DrainBackgroundMeshlets().DirtyMeshlets.Keys);
    }

    [Fact]
    public void UnsortedChunksAreRejectedRatherThanPublishedTwice()
    {
        var left = new WorldVoxelCoord(0, 0, 0);
        var right = new WorldVoxelCoord(16, 0, 0);
        Assert.Throws<ArgumentException>(() =>
            LightingPublicationPlan.Create([right, left]));
    }

    [Fact]
    public void EmptyResultNeedsNoMainThreadPublication()
    {
        Assert.Empty(LightingPublicationPlan.Create([]));
    }
    [Fact]
    public void PrioritizedPublicationStartsNearTheTriggeringEdit()
    {
        var positions = new[]
        {
            new WorldVoxelCoord(1, 2, 1),
            new WorldVoxelCoord(161, 2, 1),
            new WorldVoxelCoord(321, 2, 1),
        };
        var edit = new WorldVoxelCoord(162, 2, 1);

        var plan = LightingPublicationPlan.Create(positions, [edit]);

        Assert.Equal(new ChunkCoord(10, 0, 0), plan[0].Coord);
        Assert.Equal(ChunkCoord.Zero, plan[1].Coord);
        Assert.Equal(new ChunkCoord(20, 0, 0), plan[2].Coord);
    }

}
