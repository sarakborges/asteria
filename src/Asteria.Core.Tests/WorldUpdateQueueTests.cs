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
    public void PressuredLightingPrefersNearestResidentColumnsAndKeepsFifo()
    {
        var world = new VoxelWorld();
        var far = new ChunkCoord(-10, 0, 0);
        var near = new ChunkCoord(8, 0, 0);
        var mid = new ChunkCoord(7, 0, 1);
        foreach (var coord in new[] { far, mid, near })
            world.InsertChunk(coord, new Chunk());
        var queue = new WorldUpdateQueue();
        var farSeed = new WorldVoxelCoord(-160, 2, 0);
        var nearSeed = new WorldVoxelCoord(128, 2, 0);
        var midSeed = new WorldVoxelCoord(112, 2, 16);
        var nearLater = new WorldVoxelCoord(129, 2, 0);
        foreach (var seed in new[] { farSeed, nearSeed, midSeed, nearLater })
            queue.EnqueueLighting(seed);

        var chosen = queue.DrainLighting(
            4, 2, world, new ChunkCoord(8, 0, 1));
        Assert.Equal(new[] { nearSeed, midSeed, nearLater },
            chosen.EditedPositions);
        Assert.Equal([farSeed], queue.DrainLighting(4, 2, world)
            .EditedPositions);
        Assert.False(queue.HasLightingWork);
    }

    [Fact]
    public void PressuredLightingUsesDirectionalTieBreakDeterministically()
    {
        var world = new VoxelWorld();
        var left = new ChunkCoord(-1, 0, 0);
        var right = new ChunkCoord(1, 0, 0);
        world.InsertChunk(left, new Chunk());
        world.InsertChunk(right, new Chunk());
        var queue = new WorldUpdateQueue();
        var leftSeed = new WorldVoxelCoord(-16, 3, 0);
        var rightSeed = new WorldVoxelCoord(16, 3, 0);
        queue.EnqueueLighting(leftSeed);
        queue.EnqueueLighting(rightSeed);

        Assert.Equal([rightSeed], queue.DrainLighting(
            4, 1, world, ChunkCoord.Zero,
            new ChunkMovementDirection(1, 0)).EditedPositions);
        Assert.Equal([leftSeed], queue.DrainLighting(4, 1, world)
            .EditedPositions);
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
    public void LightingDrainIsBoundedAndPreservesRemainingOrder()
    {
        var queue =
            new WorldUpdateQueue();
        var positions =
            Enumerable.Range(
                    0,
                    5)
                .Select(index =>
                    new WorldVoxelCoord(
                        index,
                        2,
                        3))
                .ToArray();

        foreach (var position in
                 positions)
        {
            queue.EnqueueLighting(
                position);
        }

        Assert.Equal(
            positions.Take(2),
            queue.DrainLighting(
                    maximumPositions: 2)
                .EditedPositions);
        Assert.True(
            queue.HasLightingWork);
        Assert.Equal(
            positions.Skip(2),
            queue.DrainLighting()
                .EditedPositions);
        Assert.False(
            queue.HasLightingWork);
    }

    [Fact]
    public void LightingDrainBoundsDistinctColumnsWithoutReorderingOrLosingSeeds()
    {
        var queue = new WorldUpdateQueue();
        var input = new[]
        {
            new WorldVoxelCoord(0, 1, 0),
            new WorldVoxelCoord(15, 32, 15),
            new WorldVoxelCoord(16, 2, 0),
            new WorldVoxelCoord(0, 3, 16),
            new WorldVoxelCoord(48, 4, 0),
            new WorldVoxelCoord(17, 5, 1),
        };
        foreach (var position in input)
            queue.EnqueueLighting(position);

        Assert.Equal(new[] { input[0], input[1], input[2], input[5] },
            queue.DrainLighting(maximumPositions: 5, maximumColumns: 2)
                .EditedPositions);
        Assert.Equal(new[] { input[3], input[4] },
            queue.DrainLighting(maximumPositions: 2, maximumColumns: 2)
                .EditedPositions);
        Assert.Empty(queue.DrainLighting(
            maximumPositions: 256, maximumColumns: 1).EditedPositions);
        Assert.False(queue.HasLightingWork);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            queue.DrainLighting(maximumColumns: 0));
    }

    [Fact]
    public void InterleavedResidentColumnsFillBoundedLightingWorkerBatch()
    {
        var world = new VoxelWorld();
        var queue = new WorldUpdateQueue();
        for (var x = 0; x < 3; x++)
            world.InsertChunk(new ChunkCoord(x, 0, 0), new Chunk());

        var first = new WorldVoxelCoord(1, 2, 2);
        var second = new WorldVoxelCoord(17, 2, 2);
        var third = new WorldVoxelCoord(33, 2, 2);
        var firstLater = new WorldVoxelCoord(2, 2, 2);
        var secondLater = new WorldVoxelCoord(18, 2, 2);
        var thirdLater = new WorldVoxelCoord(34, 2, 2);
        foreach (var seed in new[]
                 { first, second, third, firstLater, secondLater, thirdLater })
            queue.EnqueueLighting(seed);

        Assert.Equal(new[] { first, second, firstLater, secondLater },
            queue.DrainLighting(4, 2, world).EditedPositions);
        Assert.Equal(new[] { third, thirdLater },
            queue.DrainLighting(4, 2, world).EditedPositions);
        Assert.False(queue.HasLightingWork);
    }

    [Fact]
    public void ResidentLightingDrainDropsStaleEntriesBehindDeferredColumn()
    {
        var world = new VoxelWorld();
        var queue = new WorldUpdateQueue();
        for (var x = 0; x < 3; x++)
            world.InsertChunk(new ChunkCoord(x, 0, 0), new Chunk());

        var first = new WorldVoxelCoord(1, 2, 2);
        var second = new WorldVoxelCoord(17, 2, 2);
        var deferred = new WorldVoxelCoord(33, 2, 2);
        var retired = new WorldVoxelCoord(49, 2, 2);
        var firstLater = new WorldVoxelCoord(2, 2, 2);
        foreach (var seed in new[]
                 { first, second, deferred, retired, firstLater })
            queue.EnqueueLighting(seed);

        Assert.Equal(new[] { first, second, firstLater },
            queue.DrainLighting(4, 2, world).EditedPositions);
        Assert.Equal([deferred],
            queue.DrainLighting(4, 2, world).EditedPositions);
        Assert.False(queue.HasLightingWork);
    }

    [Fact]
    public void LightingColumnScanRetainsDeferredFIFOOrderAndBoundsInspection()
    {
        var queue = new WorldUpdateQueue();
        var first = new WorldVoxelCoord(0, 1, 0);
        var second = new WorldVoxelCoord(16, 1, 0);
        queue.EnqueueLighting(first);
        queue.EnqueueLighting(second);
        for (var index = 2; index < 4100; index++)
            queue.EnqueueLighting(new WorldVoxelCoord(index * Chunk.Size, 1, 0));
        var beyondLimit = new WorldVoxelCoord(1, 1, 0);
        queue.EnqueueLighting(beyondLimit);

        // The matching seed lies outside the bounded 4096-item scan.
        Assert.Equal([first],
            queue.DrainLighting(512, 1).EditedPositions);
        Assert.True(queue.HasLightingWork);
        Assert.Equal([second],
            queue.DrainLighting(1, 1).EditedPositions);
    }

    [Fact]
    public void ResidentLightingDrainDiscardsUnloadedSeedsWithoutBlockingValidWork()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var queue = new WorldUpdateQueue();
        var stale = new WorldVoxelCoord(Chunk.Size, 2, 0);
        var valid = new WorldVoxelCoord(2, 2, 2);
        queue.EnqueueLighting(stale);
        queue.EnqueueLighting(valid);

        var batch = queue.DrainLighting(
            maximumPositions: 1, maximumColumns: 1, world: world);

        Assert.Equal([valid], batch.EditedPositions);
        Assert.False(queue.HasLightingWork);
    }

    [Fact]
    public void StaleLightingCleanupIsBoundedAndProgressesAcrossDrains()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var queue = new WorldUpdateQueue();
        for (var index = 0; index < 5000; index++)
            queue.EnqueueLighting(new WorldVoxelCoord(
                (index + 1) * Chunk.Size, 2, 0));
        var valid = new WorldVoxelCoord(2, 2, 2);
        queue.EnqueueLighting(valid);

        Assert.Empty(queue.DrainLighting(
            maximumPositions: 512, maximumColumns: 2, world: world)
            .EditedPositions);
        Assert.True(queue.HasLightingWork);
        Assert.Equal([valid], queue.DrainLighting(
            maximumPositions: 512, maximumColumns: 2, world: world)
            .EditedPositions);
        Assert.False(queue.HasLightingWork);
    }

    [Fact]
    public void StaleLightingResultRequeueDoesNotResurrectRetiredPositions()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var valid = new WorldVoxelCoord(2, 2, 2);
        var retired = new WorldVoxelCoord(Chunk.Size, 2, 2);
        var queue = new WorldUpdateQueue();

        queue.RequeueLighting(new WorldLightingBatch([retired, valid]), world);

        Assert.Equal([valid], queue.DrainLighting().EditedPositions);
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
    [Fact]
    public void InteractiveLightingEditDrainsSeparatelyBeforeBackgroundWork()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var queue = new WorldUpdateQueue();
        var olderBackground = new WorldVoxelCoord(2, 2, 2);
        var edited = new WorldVoxelCoord(3, 2, 2);
        var newerBackground = new WorldVoxelCoord(4, 2, 2);

        queue.EnqueueLighting(olderBackground);
        queue.EnqueueVoxelEdit(world, edited);
        queue.EnqueueLighting(newerBackground);

        var interactive = queue.DrainLighting(
            maximumPositions: 512,
            maximumColumns: 2,
            world,
            ChunkCoord.Zero);

        Assert.Equal([edited], interactive.EditedPositions);
        Assert.True(queue.HasLightingWork);

        Assert.Equal(
            new[] { olderBackground, newerBackground },
            queue.DrainLighting(
                maximumPositions: 512,
                maximumColumns: 2,
                world).EditedPositions);
        Assert.False(queue.HasLightingWork);
    }

    [Fact]
    public void PromotingBackgroundLightingSeedDoesNotLeaveDuplicate()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var queue = new WorldUpdateQueue();
        var edited = new WorldVoxelCoord(2, 2, 2);

        queue.EnqueueLighting(edited);
        queue.EnqueueVoxelEdit(world, edited);

        Assert.Equal([edited], queue.DrainLighting(
            maximumPositions: 512,
            maximumColumns: 2,
            world).EditedPositions);
        Assert.False(queue.HasLightingWork);
    }

}
