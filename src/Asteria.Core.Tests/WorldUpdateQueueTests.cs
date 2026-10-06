using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldUpdateQueueTests
{
    [Fact]
    public void GeometryAndLightingCanDrainIndependently()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var queue = new WorldUpdateQueue();
        var position = new WorldVoxelCoord(3, 3, 3);

        queue.EnqueueVoxelEdit(world, position);

        var mesh = queue.DrainMeshlets();

        Assert.False(mesh.IsEmpty);
        Assert.True(queue.HasLightingWork);
        Assert.False(queue.HasMeshWork);

        var lighting = queue.DrainLighting();

        Assert.Equal([position], lighting.EditedPositions);
        Assert.False(queue.HasWork);
    }

    [Fact]
    public void LightingDrainPreservesFirstEnqueueOrderAndDeduplicates()
    {
        var queue = new WorldUpdateQueue();
        var first = new WorldVoxelCoord(4, 2, 8);
        var second = new WorldVoxelCoord(1, 2, 3);

        queue.EnqueueLighting(first);
        queue.EnqueueLighting(second);
        queue.EnqueueLighting(first);

        Assert.Equal(
            new[] { first, second },
            queue.DrainLighting().EditedPositions);
    }

    [Fact]
    public void LightingOnlyRemeshDoesNotCreateAnotherLightingEdit()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var queue = new WorldUpdateQueue();

        queue.EnqueueVoxelMeshlets(
            world,
            new WorldVoxelCoord(3, 3, 3));

        Assert.True(queue.HasMeshWork);
        Assert.False(queue.HasLightingWork);
    }
    [Fact]
    public void InteractiveVoxelEditPreemptsBackgroundMeshlets()
    {
        var world =
            new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var queue =
            new WorldUpdateQueue();

        queue.EnqueueMeshlets(
            ChunkCoord.Zero,
            ChunkMeshletMask.All);
        queue.EnqueueVoxelEdit(
            world,
            new WorldVoxelCoord(
                2,
                2,
                2));

        var first =
            queue.DrainMeshlets(
                maximumMeshlets: 1);

        Assert.Equal(
            ChunkMeshletMask.Single(0),
            first.DirtyMeshlets[
                ChunkCoord.Zero]);
        Assert.True(queue.HasMeshWork);

        var rest =
            queue.DrainMeshlets();

        Assert.False(
            rest.DirtyMeshlets[
                ChunkCoord.Zero]
                .ContainsIndex(0));
    }

    [Fact]
    public void InteractiveAndBackgroundMeshLanesDrainIndependently()
    {
        var world =
            new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var queue =
            new WorldUpdateQueue();

        queue.EnqueueMeshlets(
            ChunkCoord.Zero,
            ChunkMeshletMask.All);
        queue.EnqueueVoxelEdit(
            world,
            new WorldVoxelCoord(
                2,
                2,
                2));

        var background =
            queue.DrainBackgroundMeshlets(
                maximumMeshlets: 1);

        Assert.Equal(
            1,
            background.DirtyMeshlets
                .Values
                .Sum(mask =>
                    mask.SelectedCount));
        Assert.True(
            queue.HasPriorityMeshWork);

        var interactive =
            queue.DrainPriorityMeshlets(
                maximumMeshlets: 1);

        Assert.Equal(
            ChunkMeshletMask.Single(0),
            interactive.DirtyMeshlets[
                ChunkCoord.Zero]);
        Assert.False(
            queue.HasPriorityMeshWork);
        Assert.True(
            queue.HasBackgroundMeshWork);
    }

    [Fact]
    public void RequeuePreservesMeshLane()
    {
        var queue =
            new WorldUpdateQueue();
        var batch =
            new WorldMeshBatch(
                new Dictionary<ChunkCoord, ChunkMeshletMask>
                {
                    [ChunkCoord.Zero] =
                        ChunkMeshletMask.Single(3),
                });

        queue.RequeueBackgroundMeshlets(
            batch);

        Assert.False(
            queue.HasPriorityMeshWork);
        Assert.True(
            queue.HasBackgroundMeshWork);

        var background =
            queue.DrainBackgroundMeshlets();

        queue.RequeuePriorityMeshlets(
            background);

        Assert.True(
            queue.HasPriorityMeshWork);
        Assert.False(
            queue.HasBackgroundMeshWork);
    }

    [Fact]
    public void MeshDrainContinuesInDeterministicChunkOrderAcrossBatches()
    {
        var queue =
            new WorldUpdateQueue();

        queue.EnqueueMeshlets(
            new ChunkCoord(1, 1, 0),
            ChunkMeshletMask.Single(0));
        queue.EnqueueMeshlets(
            new ChunkCoord(0, 0, 1),
            ChunkMeshletMask.Single(0));
        queue.EnqueueMeshlets(
            new ChunkCoord(0, 0, 0),
            ChunkMeshletMask.Single(0));

        var first =
            queue.DrainBackgroundMeshlets(
                maximumMeshlets: 1);
        var second =
            queue.DrainBackgroundMeshlets(
                maximumMeshlets: 1);
        var third =
            queue.DrainBackgroundMeshlets(
                maximumMeshlets: 1);

        Assert.Equal(
            ChunkCoord.Zero,
            Assert.Single(first.DirtyMeshlets).Key);
        Assert.Equal(
            new ChunkCoord(0, 0, 1),
            Assert.Single(second.DirtyMeshlets).Key);
        Assert.Equal(
            new ChunkCoord(1, 1, 0),
            Assert.Single(third.DirtyMeshlets).Key);
        Assert.False(
            queue.HasBackgroundMeshWork);
    }

    [Fact]
    public void MeshDrainHonorsWorkerBatchLimit()
    {
        var queue =
            new WorldUpdateQueue();

        queue.EnqueueMeshlets(
            ChunkCoord.Zero,
            ChunkMeshletMask.All);

        var first =
            queue.DrainMeshlets(
                maximumMeshlets: 3);

        Assert.Equal(
            3,
            first.DirtyMeshlets
                .Values
                .Sum(mask =>
                    mask.SelectedCount));
        Assert.True(queue.HasMeshWork);

        var second =
            queue.DrainMeshlets();

        Assert.Equal(
            ChunkMeshletMask.Count - 3,
            second.DirtyMeshlets
                .Values
                .Sum(mask =>
                    mask.SelectedCount));
        Assert.False(queue.HasMeshWork);
    }

}

public sealed class MeshletContentRevisionsTests
{
    [Fact]
    public void UnrelatedMeshletEditDoesNotInvalidateCapturedRevision()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var revisions = new MeshletContentRevisions();

        var first = new WorldVoxelCoord(2, 2, 2);
        revisions.BumpVoxelEdit(world, first);

        var key = new ChunkMeshletKey(ChunkCoord.Zero, 0);
        var captured = revisions.Get(key);

        revisions.BumpVoxelEdit(
            world,
            new WorldVoxelCoord(12, 12, 12));

        Assert.True(revisions.IsCurrent(key, captured));
    }

    [Fact]
    public void RemovedChunkDropsMeshletRevisionState()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var revisions =
            new MeshletContentRevisions();
        var key =
            new ChunkMeshletKey(
                ChunkCoord.Zero,
                0);

        revisions.BumpVoxelEdit(
            world,
            new WorldVoxelCoord(2, 2, 2));

        Assert.True(revisions.Get(key) > 0);

        revisions.RemoveChunk(
            ChunkCoord.Zero);

        Assert.Equal(
            0UL,
            revisions.Get(key));
    }

    [Fact]
    public void SameMeshletEditInvalidatesCapturedRevision()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var revisions = new MeshletContentRevisions();
        var key = new ChunkMeshletKey(ChunkCoord.Zero, 0);

        revisions.BumpVoxelEdit(
            world,
            new WorldVoxelCoord(2, 2, 2));
        var captured = revisions.Get(key);

        revisions.BumpVoxelEdit(
            world,
            new WorldVoxelCoord(3, 3, 3));

        Assert.False(revisions.IsCurrent(key, captured));
    }
}
