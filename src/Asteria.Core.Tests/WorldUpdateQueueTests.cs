using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldUpdateQueueTests
{
    [Fact]
    public void InteractiveDrainChoosesNearestChunkBeforeCoordinateOrder()
    {
        var queue = new WorldUpdateQueue();
        var near = new ChunkCoord(101, 0, 100);
        var distant = ChunkCoord.Zero;
        queue.EnqueuePriorityMeshlets(distant, ChunkMeshletMask.Single(0));
        queue.EnqueuePriorityMeshlets(near, ChunkMeshletMask.Single(2));

        Assert.Equal(near, Assert.Single(queue.DrainPriorityMeshlets(
            maximumMeshlets: 1, center: new ChunkCoord(100, 0, 100))
            .DirtyMeshlets).Key);
        Assert.Equal(distant, Assert.Single(queue.DrainPriorityMeshlets(
            maximumMeshlets: 1).DirtyMeshlets).Key);
    }

    [Fact]
    public void InteractiveDrainUsesDirectionToBreakDistanceTies()
    {
        var queue = new WorldUpdateQueue();
        var left = new ChunkCoord(-1, 0, 0);
        var right = new ChunkCoord(1, 0, 0);
        queue.EnqueuePriorityMeshlets(left, ChunkMeshletMask.Single(0));
        queue.EnqueuePriorityMeshlets(right, ChunkMeshletMask.Single(0));

        Assert.Equal(right, Assert.Single(queue.DrainPriorityMeshlets(
            1, ChunkCoord.Zero, new ChunkMovementDirection(1, 0))
            .DirtyMeshlets).Key);
        Assert.Equal(left, Assert.Single(queue.DrainPriorityMeshlets(
            1, ChunkCoord.Zero).DirtyMeshlets).Key);
    }

    [Fact]
    public void PriorityMeshDrainCapsSourceChunksAndPreservesRemainingWork()
    {
        var queue = new WorldUpdateQueue();
        for (var x = 0; x < 6; x++)
            queue.EnqueuePriorityMeshlets(
                new ChunkCoord(x, 0, 0), ChunkMeshletMask.Single(x));

        var first = queue.DrainPriorityMeshlets(
            maximumMeshlets: 16, maximumChunks: 2);
        Assert.Equal(2, first.DirtyMeshlets.Count);
        Assert.True(queue.HasPriorityMeshWork);

        var second = queue.DrainPriorityMeshlets(
            maximumMeshlets: 16, maximumChunks: 2);
        Assert.Equal(2, second.DirtyMeshlets.Count);
        Assert.True(queue.HasPriorityMeshWork);

        var third = queue.DrainPriorityMeshlets(
            maximumMeshlets: 16, maximumChunks: 2);
        Assert.Equal(2, third.DirtyMeshlets.Count);
        Assert.False(queue.HasPriorityMeshWork);
    }

    [Fact]
    public void AllLaneMeshDrainSharesSourceChunkCapAcrossPriorityAndBackgroundWork()
    {
        var queue = new WorldUpdateQueue();
        queue.EnqueuePriorityMeshlets(
            new ChunkCoord(0, 0, 0), ChunkMeshletMask.Single(0));
        queue.EnqueueMeshlets(
            new ChunkCoord(1, 0, 0), ChunkMeshletMask.Single(1));
        queue.EnqueueMeshlets(
            new ChunkCoord(2, 0, 0), ChunkMeshletMask.Single(2));

        var batch = queue.DrainMeshlets(
            maximumMeshlets: 16, maximumChunks: 2);

        Assert.Equal(2, batch.DirtyMeshlets.Count);
        Assert.True(queue.HasBackgroundMeshWork);
    }

    [Fact]
    public void FluidMeshDrainCapsSourceChunksAndPreservesRemainingWork()
    {
        var queue = new FluidMeshUpdateQueue();
        for (var x = 0; x < 6; x++)
            queue.EnqueueMeshlets(
                new ChunkCoord(x, 0, 0), ChunkMeshletMask.Single(x));

        var first = queue.Drain(
            maximumMeshlets: 16, maximumChunks: 2);
        Assert.Equal(2, first.DirtyMeshlets.Count);
        Assert.True(queue.HasWork);

        var second = queue.Drain(
            maximumMeshlets: 16, maximumChunks: 2);
        Assert.Equal(2, second.DirtyMeshlets.Count);
        Assert.True(queue.HasWork);

        var third = queue.Drain(
            maximumMeshlets: 16, maximumChunks: 2);
        Assert.Equal(2, third.DirtyMeshlets.Count);
        Assert.False(queue.HasWork);
    }

    [Fact]
    public void RangeInvalidationMatchesTheExactFullPositionEnumeration()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        var positions = new[]
        {
            new WorldVoxelCoord(0, 0, 0),
            new WorldVoxelCoord(7, 7, 7),
            new WorldVoxelCoord(15, 8, 8),
            new WorldVoxelCoord(16, 8, 8),
            new WorldVoxelCoord(25, 10, 10),
        };

        var ranged = MeshletInvalidation.ForPositions(world, positions, 1, 3);
        var expected = MeshletInvalidation.ForPositions(world, positions.Skip(1).Take(3));
        Assert.Equal(expected.Count, ranged.Count);
        foreach (var (coord, mask) in expected)
            Assert.Equal(mask, ranged[coord]);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MeshletInvalidation.ForPositions(world, positions, -1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            MeshletInvalidation.ForPositions(world, positions, 2, 4));
    }

    [Fact]
    public void DenseLightingInvalidationSaturatesTheChunkMeshletMask()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());

        var positions = Enumerable.Range(0, Chunk.Size)
            .SelectMany(y => Enumerable.Range(0, Chunk.Size)
                .SelectMany(z => Enumerable.Range(0, Chunk.Size)
                    .Select(x => new WorldVoxelCoord(x, y, z))))
            .ToArray();
        var dirty = MeshletInvalidation.ForPositions(world, positions);

        var entry = Assert.Single(dirty);
        Assert.Equal(ChunkCoord.Zero, entry.Key);
        Assert.Equal(ChunkMeshletMask.All, entry.Value);
    }

        [Fact]
    public void MeshWorkQueriesOnlyMatchChunksWithQueuedMeshlets()
    {
        var queue = new WorldUpdateQueue();
        var distant = new ChunkCoord(5, 0, 0);
        queue.EnqueueMeshlets(ChunkCoord.Zero, ChunkMeshletMask.Single(0));
        queue.EnqueuePriorityMeshlets(distant, ChunkMeshletMask.Single(1));

        Assert.True(queue.HasMeshWorkInChunks(coord => coord == ChunkCoord.Zero));
        Assert.True(queue.HasMeshWorkInChunks(coord => coord == distant));
        Assert.False(queue.HasMeshWorkInChunks(coord => coord == new ChunkCoord(2, 0, 0)));
    }

        [Fact]
    public void ScannedQueuePreservesSkippedEntriesAndDropsObsoleteOnes()
    {
        var queue = new DeduplicatedQueue<int>();
        foreach (var value in new[] { 1, 2, 3, 4, 5 })
            queue.Enqueue(value);
        var revision = queue.Revision;
        Assert.Equal(new[] { 1, 3 }, queue.DrainScanned(
            3, 4, value => value == 2
                ? QueueScanDecision.Keep
                : value == 4
                    ? QueueScanDecision.Discard
                    : QueueScanDecision.Take));
        Assert.True(queue.Revision > revision);
        Assert.Equal(new[] { 2, 5 }, queue.ValuesInOrder());
    }

    [Fact]
    public void PrefixVisitIsBoundedAndDoesNotMutateQueue()
    {
        var queue = new DeduplicatedQueue<int>();
        foreach (var value in new[] { 1, 2, 3 })
            queue.Enqueue(value);
        var revision = queue.Revision;
        var seen = new List<int>();
        queue.VisitPrefix(2, seen.Add);
        Assert.Equal([1, 2], seen);
        Assert.Equal([1, 2, 3], queue.ValuesInOrder());
        Assert.Equal(revision, queue.Revision);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            queue.VisitPrefix(0, seen.Add));
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
    public void BecomingVisiblePromotesOnlyPendingBackgroundMeshlets()
    {
        var queue = new WorldUpdateQueue();
        queue.EnqueueMeshlets(ChunkCoord.Zero, ChunkMeshletMask.All);
        queue.EnqueuePriorityMeshlets(
            ChunkCoord.Zero, ChunkMeshletMask.Single(0));

        var visibleMissing = new ChunkMeshletMask(0b00000111);
        Assert.Equal(
            new ChunkMeshletMask(0b00000110),
            queue.PromoteBackgroundMeshlets(
                ChunkCoord.Zero, visibleMissing));
        Assert.Equal(
            ChunkMeshletMask.None,
            queue.PromoteBackgroundMeshlets(
                ChunkCoord.Zero, visibleMissing));

        var urgent = queue.DrainPriorityMeshlets();
        Assert.Equal(visibleMissing,
            urgent.DirtyMeshlets[ChunkCoord.Zero]);

        var background = queue.DrainBackgroundMeshlets();
        Assert.Equal(
            ChunkMeshletMask.All.Except(visibleMissing),
            background.DirtyMeshlets[ChunkCoord.Zero]);
        Assert.False(queue.HasMeshWork);
    }

    [Fact]
    public void VisibilityPromotionDoesNotDuplicateWorkAlreadyInFlight()
    {
        var queue = new WorldUpdateQueue();
        queue.EnqueueMeshlets(
            ChunkCoord.Zero, ChunkMeshletMask.Single(2));
        var inFlight = queue.DrainBackgroundMeshlets();
        Assert.False(inFlight.IsEmpty);

        Assert.Equal(
            ChunkMeshletMask.None,
            queue.PromoteBackgroundMeshlets(
                ChunkCoord.Zero, ChunkMeshletMask.All));
        Assert.False(queue.HasMeshWork);

        queue.RequeueBackgroundMeshlets(inFlight);
        Assert.Equal(
            ChunkMeshletMask.Single(2),
            queue.PromoteBackgroundMeshlets(
                ChunkCoord.Zero, ChunkMeshletMask.All));
        Assert.Equal(
            ChunkMeshletMask.Single(2),
            queue.DrainPriorityMeshlets().DirtyMeshlets[ChunkCoord.Zero]);
        Assert.False(queue.HasMeshWork);
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
    public void RemovingOneChunkKeepsOtherChunkRevisions()
    {
        var revisions =
            new MeshletContentRevisions();
        var kept =
            new ChunkCoord(
                1,
                0,
                0);
        var removed =
            ChunkCoord.Zero;
        var keptKey =
            new ChunkMeshletKey(
                kept,
                3);
        var removedKey =
            new ChunkMeshletKey(
                removed,
                2);

        revisions.Bump(
            kept,
            ChunkMeshletMask.Single(3));
        revisions.Bump(
            removed,
            ChunkMeshletMask.Single(2));

        revisions.RemoveChunk(
            removed);

        Assert.Equal(
            0UL,
            revisions.Get(removedKey));
        Assert.Equal(
            1UL,
            revisions.Get(keptKey));
    }

    [Fact]
    public void InvalidMeshletRevisionKeyIsRejected()
    {
        var revisions =
            new MeshletContentRevisions();

        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                revisions.Get(
                    new ChunkMeshletKey(
                        ChunkCoord.Zero,
                        ChunkMeshletMask.Count)));
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
