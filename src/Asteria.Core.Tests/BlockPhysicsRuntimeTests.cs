using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockPhysicsRuntimeTests
{
    [Fact]
    public void VoxelEditWakesChangedVoxelAndAllSupportNeighborsOnce()
    {
        var queue =
            new BlockPhysicsUpdateQueue();
        var position =
            new WorldVoxelCoord(3, 7, -2);

        queue.EnqueueVoxelEdit(position);
        queue.EnqueueVoxelEdit(position);

        Assert.Equal(
            new[]
            {
                position,
                position + (0, 1, 0),
                position + (0, -1, 0),
                position + (1, 0, 0),
                position + (-1, 0, 0),
                position + (0, 0, 1),
                position + (0, 0, -1),
            },
            queue.DrainBatch());
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void PhysicsWakeupsAreBoundedPerTickAndPreserveFifoRemainder()
    {
        var queue = new BlockPhysicsUpdateQueue();
        var positions = Enumerable.Range(0, BlockPhysicsUpdateQueue.MaximumBatchSize + 7)
            .Select(x => new WorldVoxelCoord(x, 5, 0))
            .ToArray();

        foreach (var position in positions)
            queue.Enqueue(position);
        var first = queue.DrainBatch();

        Assert.Equal(positions.Take(BlockPhysicsUpdateQueue.MaximumBatchSize), first);
        Assert.Equal(7, queue.Count);
        Assert.Equal(positions.Skip(BlockPhysicsUpdateQueue.MaximumBatchSize),
            queue.DrainBatch());
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void UnloadedDependenciesSleepUntilThatChunkArrives()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:wall_plant",
                tags: [BlockPhysicsCapabilities.SupportWall],
                isCollidable: false),
        ]);
        var updates = new BlockPhysicsUpdateQueue();
        var mutations = new VoxelMutationRuntime(world,
            new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), updates,
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        using var runtime = new BlockPhysicsRuntime(
            world, blocks, mutations, updates, drops);
        var position = new WorldVoxelCoord(Chunk.Size - 1, 5, 5);
        var waitingChunk = new ChunkCoord(1, 0, 0);

        Assert.True(mutations.SetCellAt(position,
            new VoxelCell(blocks.GetId("asteria:wall_plant"),
                facing: HorizontalFacing.West), out _));
        runtime.ProcessWakeups();

        Assert.Equal(0, updates.Count);
        Assert.Equal(1, updates.WaitingCount);
        for (var tick = 0; tick < 10; tick++)
            Assert.Equal(BlockPhysicsWakeResult.Empty, runtime.ProcessWakeups());

        // The legacy queue snapshot can represent this dormant dependency;
        // live session saves now reconstruct it from voxel state on chunk arrival.
        Assert.Contains(position, updates.CaptureState().Positions);

        var support = new Chunk();
        support.SetBlock(0, 5, 5, blocks.GetId("asteria:stone"));
        world.InsertChunk(waitingChunk, support);

        Assert.Equal(0, updates.WaitingCount);
        Assert.Contains(position, updates.DrainBatch());
        // A new support remains authoritative after the dependency resolves.
        updates.Enqueue(position);
        Assert.Equal(0, runtime.ProcessWakeups().UnsupportedRemoved);
        Assert.False(world.GetCellOrEmpty(position).IsEmpty);
    }

    [Fact]
    public void ForgetSourceChunkRemovesItsQueuedAndDormantWakeups()
    {
        var queue = new BlockPhysicsUpdateQueue();
        var queuedInSource = new WorldVoxelCoord(3, 5, 2);
        var waitingInSource = new WorldVoxelCoord(4, 5, 2);
        var queuedElsewhere = new WorldVoxelCoord(Chunk.Size + 2, 5, 2);

        queue.Enqueue(queuedInSource);
        queue.WaitForChunk(waitingInSource, new ChunkCoord(2, 0, 0));
        queue.Enqueue(queuedElsewhere);

        Assert.Equal(2, queue.Count);
        Assert.Equal(1, queue.WaitingCount);
        Assert.Equal(2, queue.ForgetSourceChunk(ChunkCoord.Zero));
        Assert.Equal(1, queue.Count);
        Assert.Equal(0, queue.WaitingCount);
        Assert.Equal(new[] { queuedElsewhere }, queue.DrainBatch());
    }

    [Fact]
    public void ArchivingSourceChunkDropsDormantSupportChecks()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:wall_plant",
                tags: [BlockPhysicsCapabilities.SupportWall],
                isCollidable: false),
        ]);
        var updates = new BlockPhysicsUpdateQueue();
        var mutations = new VoxelMutationRuntime(world,
            new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), updates,
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        using var runtime = new BlockPhysicsRuntime(
            world, blocks, mutations, updates, drops);
        var position = new WorldVoxelCoord(Chunk.Size - 1, 5, 5);

        Assert.True(mutations.SetCellAt(position,
            new VoxelCell(blocks.GetId("asteria:wall_plant"),
                facing: HorizontalFacing.West), out _));
        runtime.ProcessWakeups();

        Assert.Equal(1, updates.WaitingCount);
        Assert.NotEqual(
            ChunkArchiveResult.NotResident,
            world.ArchiveChunk(ChunkCoord.Zero));
        Assert.Equal(0, updates.Count);
        Assert.Equal(0, updates.WaitingCount);
    }

    [Fact]
    public void PhysicsWaitersCanBeReactivatedByVoxelEdits()
    {
        var queue = new BlockPhysicsUpdateQueue();
        var position = new WorldVoxelCoord(15, 3, 4);
        var missing = new ChunkCoord(1, 0, 0);
        queue.WaitForChunk(position, missing);

        queue.EnqueueVoxelEdit(position);
        Assert.Equal(0, queue.WaitingCount);
        Assert.Contains(position, queue.DrainBatch());
        queue.WakeResidentChunk(missing);
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void PhysicsWaitersKeepSeparateReasonsAndClearCountsWhenWoken()
    {
        var queue = new BlockPhysicsUpdateQueue();
        var supportPosition = new WorldVoxelCoord(3, 5, 2);
        var gravityPosition = new WorldVoxelCoord(4, 5, 2);
        var neededChunk = new ChunkCoord(1, 0, 0);

        queue.WaitForChunk(
            supportPosition, neededChunk, BlockPhysicsWaitReason.SupportDependency);
        queue.WaitForChunk(
            gravityPosition, neededChunk, BlockPhysicsWaitReason.GravityBelowChunk);

        Assert.Equal(2, queue.WaitingCount);
        Assert.Equal(1, queue.WaitingSupportCount);
        Assert.Equal(1, queue.WaitingGravityCount);

        // Reclassifying an existing dependency must not double-count it.
        queue.WaitForChunk(
            gravityPosition, neededChunk, BlockPhysicsWaitReason.SupportDependency);
        Assert.Equal(2, queue.WaitingSupportCount);
        Assert.Equal(0, queue.WaitingGravityCount);

        queue.WakeResidentChunk(neededChunk);

        Assert.Equal(0, queue.WaitingCount);
        Assert.Equal(0, queue.WaitingSupportCount);
        Assert.Equal(0, queue.WaitingGravityCount);
        Assert.Equal(2, queue.Count);
    }

    [Fact]
    public void FallingBlocksDoNotAdvanceWithinChunkUntilLowerChunkIsResident()
    {
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:sand",
                tags: [BlockPhysicsCapabilities.Gravity]),
        ]);
        var updates = new BlockPhysicsUpdateQueue();
        var mutations = new VoxelMutationRuntime(world,
            new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), updates,
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        var state = BlockStateSnapshot.FromCell(
            new VoxelCell(blocks.GetId("asteria:sand")));
        var original = new BlockPhysicsRuntimeSnapshot(1, [
            new FallingBlockState(new FallingBlockId(1), state,
                3, 3, 25.5, -2.0),
        ]);
        using var physics = new BlockPhysicsRuntime(
            world, blocks, mutations, updates, drops, restore: original);

        for (var frame = 0; frame < 60; frame++)
            physics.Advance(1.0 / 60.0, 18.0);

        var parked = Assert.Single(physics.ActiveBlocks);
        Assert.Equal(25.5, parked.CenterY);
        Assert.Equal(0.0, parked.VelocityY);
        Assert.Equal(1, physics.WaitingFallingCount);

        // Loading the lower chunk wakes the entity without losing its state.
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        Assert.Equal(0, physics.WaitingFallingCount);
        physics.Advance(1.0 / 60.0, 18.0);

        var resumed = Assert.Single(physics.ActiveBlocks);
        Assert.True(resumed.CenterY < 25.5);
        Assert.Equal(0, physics.WaitingFallingCount);
    }

    [Fact]
    public void FallingBlocksParkOnUnloadedSupportAndResumeOnlyWhenItArrives()
    {
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());
        world.InsertChunk(new ChunkCoord(1, 1, 0), new Chunk());
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:sand",
                tags: [BlockPhysicsCapabilities.Gravity]),
        ]);
        var updates = new BlockPhysicsUpdateQueue();
        var mutations = new VoxelMutationRuntime(world,
            new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), updates,
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        var state = BlockStateSnapshot.FromCell(
            new VoxelCell(blocks.GetId("asteria:sand")));
        var original = new BlockPhysicsRuntimeSnapshot(2, [
            new FallingBlockState(new FallingBlockId(1), state,
                3, 3, 16.5, 0),
            new FallingBlockState(new FallingBlockId(2), state,
                Chunk.Size + 3, 3, 16.5, 0),
        ]);
        using var physics = new BlockPhysicsRuntime(
            world, blocks, mutations, updates, drops, restore: original);

        physics.Advance(1.0 / 60.0, 18.0);
        Assert.Equal(2, physics.ActiveCount);
        Assert.Equal(2, physics.WaitingFallingCount);
        Assert.Equal(2, physics.CaptureState().ActiveBlocks.Count);

        for (var frame = 0; frame < 100; frame++)
            physics.Advance(1.0 / 60.0, 18.0);
        Assert.Equal(2, physics.WaitingFallingCount);
        Assert.All(physics.ActiveBlocks,
            block => Assert.Equal(16.5, block.CenterY));

        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        Assert.Equal(1, physics.WaitingFallingCount);
        physics.Advance(1.0 / 60.0, 18.0);
        var byId = physics.ActiveBlocks.ToDictionary(block => block.Id.Value);
        Assert.True(byId[1].CenterY < 16.5);
        Assert.Equal(16.5, byId[2].CenterY);

        world.InsertChunk(new ChunkCoord(1, 0, 0), new Chunk());
        Assert.Equal(0, physics.WaitingFallingCount);
        physics.Advance(1.0 / 60.0, 18.0);
        Assert.True(physics.ActiveBlocks.Single(block =>
            block.Id.Value == 2).CenterY < 16.5);
    }

    [Fact]
    public void RestoredFallingBlocksReevaluateMissingChunksOnFirstAdvance()
    {
        var world = new VoxelWorld();
        world.InsertChunk(new ChunkCoord(0, 1, 0), new Chunk());
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:sand",
                tags: [BlockPhysicsCapabilities.Gravity]),
        ]);
        var updates = new BlockPhysicsUpdateQueue();
        var mutations = new VoxelMutationRuntime(world,
            new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), updates,
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        var snapshot = new BlockPhysicsRuntimeSnapshot(1, [
            new FallingBlockState(new FallingBlockId(1),
                BlockStateSnapshot.FromCell(
                    new VoxelCell(blocks.GetId("asteria:sand"))),
                3, 3, 16.5, 0),
        ]);

        using (var initial = new BlockPhysicsRuntime(
            world, blocks, mutations, updates, drops, restore: snapshot))
        {
            initial.Advance(1.0 / 60.0, 18.0);
            Assert.Equal(1, initial.WaitingFallingCount);
            snapshot = initial.CaptureState();
        }

        using var restored = new BlockPhysicsRuntime(
            world, blocks, mutations, updates, drops, restore: snapshot);
        restored.Advance(1.0 / 60.0, 18.0);
        Assert.Equal(1, restored.WaitingFallingCount);

        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        Assert.Equal(0, restored.WaitingFallingCount);
        restored.Advance(1.0 / 60.0, 18.0);
        Assert.True(Assert.Single(restored.ActiveBlocks).CenterY < 16.5);
    }

    [Fact]
    public void ResidentChunkSeedsExistingGravityBlocks()
    {
        var world = new VoxelWorld();
        var chunk = new Chunk();
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:sand",
                    tags:
                    [
                        BlockPhysicsCapabilities.Gravity,
                    ]),
            ]);
        var sand =
            blocks.GetId(
                "asteria:sand");
        chunk.SetBlock(
            3,
            4,
            3,
            sand);
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var worldUpdates =
            new WorldUpdateQueue();
        var fluidUpdates =
            new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var mutations =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                new MeshletContentRevisions(),
                new MeshletContentRevisions());
        var dropped =
            new DroppedBlockRuntime(
                world,
                blocks);
        var physics =
            new BlockPhysicsRuntime(
                world,
                blocks,
                mutations,
                physicsUpdates,
                dropped);

        Assert.Equal(
            1,
            physics.EnqueueResidentChunk(
                ChunkCoord.Zero));
        Assert.Equal(
            1,
            physicsUpdates.Count);
        Assert.Equal(
            1,
            physics.ProcessWakeups().FallingStarted);
    }

    [Fact]
    public void BreakingSupportWakesGravityWithoutSeedingSupportedChunkBacklog()
    {
        var world = new VoxelWorld();
        var chunk = new Chunk();
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:sand",
                tags: [BlockPhysicsCapabilities.Gravity]),
            new BlockDefinition("asteria:stone"),
        ]);
        var sand = blocks.GetId("asteria:sand");
        var stone = blocks.GetId("asteria:stone");

        // Seed a dense resident chunk with supported gravity blocks. Chunk
        // arrival must not schedule every stable block for redundant checks.
        for (var y = 1; y < Chunk.Size; y += 2)
        for (var z = 0; z < Chunk.Size; z++)
        for (var x = 0; x < Chunk.Size; x++)
        {
            chunk.SetBlock(x, y - 1, z, stone);
            chunk.SetBlock(x, y, z, sand);
        }

        world.InsertChunk(ChunkCoord.Zero, chunk);
        var updates = new BlockPhysicsUpdateQueue();
        var mutations = new VoxelMutationRuntime(world,
            new WorldUpdateQueue(), new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), updates,
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        using var physics = new BlockPhysicsRuntime(
            world, blocks, mutations, updates, drops);

        Assert.Equal(0, physics.EnqueueResidentChunk(ChunkCoord.Zero));
        Assert.Equal(0, updates.Count);

        var target = new WorldVoxelCoord(0, Chunk.Size - 1, 0);
        var support = target + (0, -1, 0);
        var interactions = new BlockInteractionRuntime(
            world, blocks, mutations, drops);
        Assert.True(interactions.Break(
            new VoxelWorldHit(support, 0, 0, 0)).Accepted);

        var wake = physics.ProcessWakeups();

        Assert.Equal(1, wake.FallingStarted);
        var falling = Assert.Single(physics.ActiveBlocks);
        Assert.Equal(target.X, falling.ColumnX);
        Assert.Equal(target.Z, falling.ColumnZ);
        Assert.Equal(target.Y + 0.5, falling.CenterY);
    }

    [Fact]
    public void UnsupportedGravityBlockBecomesContinuousFallingState()
    {
        var fixture = CreateFixture();
        var sand =
            fixture.Blocks.GetId(
                "asteria:sand");
        var position =
            new WorldVoxelCoord(3, 4, 3);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                sand,
                out _));

        var started =
            fixture.Physics.ProcessWakeups();

        Assert.Equal(
            1,
            started.FallingStarted);
        Assert.True(
            fixture.World
                .GetCellOrEmpty(position)
                .IsEmpty);

        var falling =
            Assert.Single(
                fixture.Physics.ActiveBlocks);

        Assert.Equal(
            sand,
            falling.Cell.Block);
        Assert.Equal(
            4.5,
            falling.CenterY);
    }

    [Fact]
    public void FallingBlocksCannotOverlapWhenUpperBlockCatchesLowerBlock()
    {
        var fixture = CreateFixture();
        var sand = fixture.Blocks.GetId("asteria:sand");
        var stone = fixture.Blocks.GetId("asteria:stone");
        Assert.True(fixture.Mutations.SetBlockAt(
            new WorldVoxelCoord(3, 0, 3), stone, out _));

        // Reverse ID order deliberately: physics order must follow spatial
        // position, not the identity assigned when an entity was created.
        fixture.Physics.Dispose();
        var sandState = BlockStateSnapshot.FromCell(new VoxelCell(sand));
        var snapshot = new BlockPhysicsRuntimeSnapshot(2, [
            new FallingBlockState(new FallingBlockId(1), sandState,
                3, 3, 5.55, -24.0),
            new FallingBlockState(new FallingBlockId(2), sandState,
                3, 3, 4.5, 0.0),
        ]);
        using var physics = new BlockPhysicsRuntime(
            fixture.World, fixture.Blocks, fixture.Mutations,
            fixture.PhysicsUpdates, fixture.Dropped, restore: snapshot);

        for (var frame = 0; frame < 240 && physics.ActiveCount > 0; frame++)
        {
            physics.Advance(1.0 / 60.0, 18.0);
            var active = physics.ActiveBlocks
                .OrderBy(block => block.ColumnX)
                .ThenBy(block => block.ColumnZ)
                .ThenBy(block => block.CenterY)
                .ToArray();

            for (var index = 1; index < active.Length; index++)
            {
                var lower = active[index - 1];
                var upper = active[index];
                if (lower.ColumnX == upper.ColumnX &&
                    lower.ColumnZ == upper.ColumnZ)
                {
                    Assert.True(
                        upper.CenterY - lower.CenterY >= 1.0 - 1e-8,
                        $"Falling blocks {lower.Id.Value} and {upper.Id.Value} overlap.");
                }
            }
        }

        Assert.Equal(0, physics.ActiveCount);
        Assert.Equal(sand, fixture.World.GetCellOrEmpty(
            new WorldVoxelCoord(3, 1, 3)).Block);
        Assert.Equal(sand, fixture.World.GetCellOrEmpty(
            new WorldVoxelCoord(3, 2, 3)).Block);
    }

    [Fact]
    public void VoxelMutationCannotPlaceABlockInsideAFallingBlock()
    {
        var fixture = CreateFixture();
        var sand = fixture.Blocks.GetId("asteria:sand");
        var stone = fixture.Blocks.GetId("asteria:stone");
        var fallingPosition = new WorldVoxelCoord(3, 4, 3);

        Assert.True(fixture.Mutations.SetBlockAt(
            fallingPosition, sand, out _));
        Assert.Equal(1, fixture.Physics.ProcessWakeups().FallingStarted);

        Assert.False(fixture.Mutations.SetBlockAt(
            fallingPosition, stone, out _));
        Assert.True(fixture.World.GetCellOrEmpty(fallingPosition).IsEmpty);
    }

    [Fact]
    public void FallingBlockMovesContinuouslyAndSettlesAboveSupport()
    {
        var fixture = CreateFixture();
        var stone =
            fixture.Blocks.GetId(
                "asteria:stone");
        var sand =
            fixture.Blocks.GetId(
                "asteria:sand");
        var support =
            new WorldVoxelCoord(3, 0, 3);
        var start =
            new WorldVoxelCoord(3, 4, 3);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                support,
                stone,
                out _));
        Assert.True(
            fixture.Mutations.SetBlockAt(
                start,
                sand,
                out _));

        fixture.Physics.ProcessWakeups();

        var previousY =
            Assert.Single(
                fixture.Physics.ActiveBlocks)
                .CenterY;

        fixture.Physics.Advance(
            1.0 / 60.0,
            18.0);

        var movedY =
            Assert.Single(
                fixture.Physics.ActiveBlocks)
                .CenterY;

        Assert.True(movedY < previousY);
        Assert.True(movedY > 1.5);

        for (var frame = 0;
             frame < 240 &&
             fixture.Physics.ActiveCount > 0;
             frame++)
        {
            fixture.Physics.Advance(
                1.0 / 60.0,
                18.0);
        }

        Assert.Equal(
            0,
            fixture.Physics.ActiveCount);
        Assert.Equal(
            sand,
            fixture.World
                .GetCellOrEmpty(
                    new WorldVoxelCoord(
                        3,
                        1,
                        3))
                .Block);
    }

    [Fact]
    public void FallingBlockPassesThroughFluidAndReplacesItOnLanding()
    {
        var fixture = CreateFixture();
        var stone =
            fixture.Blocks.GetId(
                "asteria:stone");
        var sand =
            fixture.Blocks.GetId(
                "asteria:sand");
        var support =
            new WorldVoxelCoord(3, 0, 3);
        var landing =
            new WorldVoxelCoord(3, 1, 3);
        var start =
            new WorldVoxelCoord(3, 4, 3);
        var water =
            new FluidRuntimeId(1);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                support,
                stone,
                out _));
        Assert.True(
            fixture.World.SetFluidAt(
                landing,
                FluidCell.Source(water),
                out _));
        Assert.True(
            fixture.Mutations.SetBlockAt(
                start,
                sand,
                out _));

        fixture.Physics.ProcessWakeups();

        fixture.WorldUpdates.DrainLighting();
        fixture.WorldUpdates.DrainMeshlets();
        fixture.FluidUpdates.DrainReady(
            currentTick: 0,
            maximumItems: 64);
        fixture.FluidMeshUpdates.Drain();
        fixture.PhysicsUpdates.DrainBatch();

        var landingMask =
            ChunkMeshletMask.ForWorldPosition(
                ChunkCoord.Zero,
                landing);
        var landingMeshlet =
            landingMask.Indices().First();
        var key =
            new ChunkMeshletKey(
                ChunkCoord.Zero,
                landingMeshlet);
        var before =
            fixture.FluidRevisions.Get(key);

        for (var frame = 0;
             frame < 240 &&
             fixture.Physics.ActiveCount > 0;
             frame++)
        {
            fixture.Physics.Advance(
                1.0 / 60.0,
                18.0);
        }

        Assert.Equal(
            sand,
            fixture.World
                .GetCellOrEmpty(landing)
                .Block);
        Assert.True(
            fixture.World
                .GetFluidOrEmpty(landing)
                .IsEmpty);
        Assert.Equal(
            before + 1,
            fixture.FluidRevisions.Get(key));
        Assert.Equal(
            7,
            fixture.FluidUpdates.TopologyCount);
    }

    [Fact]
    public void SculptedGravityBlockPreservesMaskAcrossFall()
    {
        var fixture = CreateFixture();
        var sand =
            fixture.Blocks.GetId(
                "asteria:sand");
        var support =
            new WorldVoxelCoord(3, 0, 3);
        var start =
            new WorldVoxelCoord(3, 4, 3);
        var mask =
            MicroblockMask.Full.Edit(
                0,
                0,
                0,
                MicroblockResolution.Thin,
                occupied: false);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                support,
                fixture.Blocks.GetId("asteria:stone"),
                out _));
        Assert.True(
            fixture.Mutations.SetBlockAt(
                start,
                sand,
                out _));

        var address =
            VoxelCoordinates.FromWorld(
                start.X,
                start.Y,
                start.Z);
        Assert.True(
            fixture.World
                .GetChunk(address.Chunk)
                .SetMicroblockMask(
                    address.Local.X,
                    address.Local.Y,
                    address.Local.Z,
                    mask));

        fixture.Physics.ProcessWakeups();

        var falling =
            Assert.Single(
                fixture.Physics.ActiveBlocks);
        Assert.Equal(
            mask,
            falling.Block.MicroblockMask);

        for (var frame = 0;
             frame < 240 &&
             fixture.Physics.ActiveCount > 0;
             frame++)
        {
            fixture.Physics.Advance(
                1.0 / 60.0,
                18.0);
        }

        var landing =
            new WorldVoxelCoord(3, 1, 3);
        Assert.Equal(
            sand,
            fixture.World
                .GetCellOrEmpty(landing)
                .Block);
        Assert.Equal(
            mask,
            fixture.World
                .GetMicroblockMaskOrEmpty(
                    landing));
    }

    [Fact]
    public void PartialSupportCellStillOwnsVoxelForGravityMaterialization()
    {
        var world =
            new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:sand",
                    tags:
                    [
                        BlockPhysicsCapabilities.Gravity,
                    ]),
                new BlockDefinition(
                    "asteria:layer",
                    shape:
                        BlockShapeDefinition.SurfaceLayer(
                            0.125f,
                            "asteria:sand")),
            ]);
        var updates =
            new BlockPhysicsUpdateQueue();
        var mutations =
            new VoxelMutationRuntime(
                world,
                new WorldUpdateQueue(),
                new FluidUpdateQueue(),
                new FluidMeshUpdateQueue(),
                updates,
                new MeshletContentRevisions(),
                new MeshletContentRevisions());
        var dropped =
            new DroppedBlockRuntime(
                world,
                blocks);
        var physics =
            new BlockPhysicsRuntime(
                world,
                blocks,
                mutations,
                updates,
                dropped);

        Assert.True(
            mutations.SetBlockAt(
                new WorldVoxelCoord(3, 0, 3),
                blocks.GetId("asteria:layer"),
                out _));
        Assert.True(
            mutations.SetBlockAt(
                new WorldVoxelCoord(3, 1, 3),
                blocks.GetId("asteria:sand"),
                out _));

        var wake =
            physics.ProcessWakeups();

        Assert.Equal(
            0,
            wake.FallingStarted);
        Assert.Equal(
            blocks.GetId("asteria:sand"),
            world.GetCellOrEmpty(
                    new WorldVoxelCoord(
                        3,
                        1,
                        3))
                .Block);
    }

    [Fact]
    public void FallingCapacityKeepsAdditionalBlockAuthoritative()
    {
        var fixture =
            CreateFixture(
                maximumActive: 1);
        var sand =
            fixture.Blocks.GetId(
                "asteria:sand");
        var first =
            new WorldVoxelCoord(
                3,
                4,
                3);
        var second =
            new WorldVoxelCoord(
                4,
                4,
                3);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                first,
                sand,
                out _));
        Assert.True(
            fixture.Mutations.SetBlockAt(
                second,
                sand,
                out _));

        var wake =
            fixture.Physics.ProcessWakeups();

        Assert.Equal(
            1,
            wake.FallingStarted);
        Assert.Equal(
            1,
            fixture.Physics.ActiveCount);
        var firstEmpty =
            fixture.World.GetCellOrEmpty(first).IsEmpty;
        var secondEmpty =
            fixture.World.GetCellOrEmpty(second).IsEmpty;
        Assert.NotEqual(firstEmpty, secondEmpty);

        var pendingPosition = firstEmpty ? second : first;
        Assert.Equal(
            sand,
            fixture.World.GetCellOrEmpty(pendingPosition).Block);
        Assert.True(fixture.PhysicsUpdates.Count > 0);

        var saturatedWake =
            fixture.Physics.ProcessWakeups();

        Assert.Equal(
            0,
            saturatedWake.FallingStarted);
        Assert.Equal(
            sand,
            fixture.World.GetCellOrEmpty(pendingPosition).Block);

        for (var frame = 0;
             frame < 240 && fixture.Physics.ActiveCount > 0;
             frame++)
        {
            fixture.Physics.Advance(1.0 / 60.0, 18.0);
        }

        Assert.Equal(0, fixture.Physics.ActiveCount);
        Assert.Equal(1, fixture.Physics.ProcessWakeups().FallingStarted);
        for (var frame = 0;
             frame < 240 && fixture.Physics.ActiveCount > 0;
             frame++)
        {
            fixture.Physics.Advance(1.0 / 60.0, 18.0);
        }

        Assert.Equal(0, fixture.Physics.ActiveCount);
        Assert.Equal(sand, fixture.World.GetCellOrEmpty(
            new WorldVoxelCoord(first.X, 0, first.Z)).Block);
        Assert.Equal(sand, fixture.World.GetCellOrEmpty(
            new WorldVoxelCoord(second.X, 0, second.Z)).Block);
    }

    [Fact]
    public void NonGravityBlockDoesNotStartFalling()
    {
        var fixture = CreateFixture();
        var stone =
            fixture.Blocks.GetId(
                "asteria:stone");
        var position =
            new WorldVoxelCoord(3, 4, 3);

        Assert.True(
            fixture.Mutations.SetBlockAt(
                position,
                stone,
                out _));

        Assert.Equal(
            0,
            fixture.Physics.ProcessWakeups().FallingStarted);
        Assert.Equal(
            0,
            fixture.Physics.ActiveCount);
        Assert.Equal(
            stone,
            fixture.World
                .GetCellOrEmpty(position)
                .Block);
    }

    [Fact]
    public void UnsupportedSupportBelowBlockIsRemovedAndReported()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:layer_snow",
                    tags:
                    [
                        BlockPhysicsCapabilities.SupportBelow,
                    ],
                    shape:
                        BlockShapeDefinition.SurfaceLayer(
                            0.125f,
                            "asteria:snow")),
                new BlockDefinition("asteria:snow"),
            ]);
        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var mutations =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                new MeshletContentRevisions(),
                new MeshletContentRevisions());
        var dropped =
            new DroppedBlockRuntime(
                world,
                blocks);
        var physics =
            new BlockPhysicsRuntime(
                world,
                blocks,
                mutations,
                physicsUpdates,
                dropped);
        var position =
            new WorldVoxelCoord(3, 4, 3);

        Assert.True(
            mutations.SetBlockAt(
                position,
                blocks.GetId("asteria:layer_snow"),
                out _));

        var wake =
            physics.ProcessWakeups();

        Assert.Equal(
            1,
            wake.UnsupportedRemoved);
        var drop =
            Assert.Single(
                dropped.ActiveBlocks);
        Assert.Equal(
            blocks.GetId("asteria:layer_snow"),
            drop.Block.Cell.Block);
        Assert.True(
            world.GetCellOrEmpty(position).IsEmpty);
    }

    [Fact]
    public void SupportedGroundPlantPopsOffAfterSupportBreak()
    {
        var world =
            new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:mushroom_brown",
                    tags:
                    [
                        BlockPhysicsCapabilities.SupportBelow,
                    ],
                    isCollidable: false),
            ]);
        var worldUpdates =
            new WorldUpdateQueue();
        var fluidUpdates =
            new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var mutations =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                new MeshletContentRevisions(),
                new MeshletContentRevisions());
        var dropped =
            new DroppedBlockRuntime(
                world,
                blocks);
        var physics =
            new BlockPhysicsRuntime(
                world,
                blocks,
                mutations,
                physicsUpdates,
                dropped);
        var support =
            new WorldVoxelCoord(
                3,
                2,
                3);
        var plant =
            support + (0, 1, 0);

        Assert.True(
            mutations.SetBlockAt(
                support,
                blocks.GetId(
                    "asteria:stone"),
                out _));
        Assert.True(
            mutations.SetBlockAt(
                plant,
                blocks.GetId(
                    "asteria:mushroom_brown"),
                out _));

        physics.ProcessWakeups();
        Assert.False(
            world.GetCellOrEmpty(
                    plant)
                .IsEmpty);

        Assert.True(
            mutations.SetCellAt(
                support,
                VoxelCell.Empty,
                out _));

        var wake =
            physics.ProcessWakeups();

        Assert.Equal(
            1,
            wake.UnsupportedRemoved);
        Assert.True(
            world.GetCellOrEmpty(
                    plant)
                .IsEmpty);
        Assert.Single(
            dropped.ActiveBlocks);
    }

    [Fact]
    public void WallFloraIsRemovedWhenTheRecordedSupportFaceIsMined()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:moss_wall",
                tags: [BlockPhysicsCapabilities.SupportWall],
                isCollidable:false)
        ]);
        var updates = new WorldUpdateQueue();
        var fluids = new FluidUpdateQueue();
        var meshes = new FluidMeshUpdateQueue();
        var physicsQueue = new BlockPhysicsUpdateQueue();
        var mutations = new VoxelMutationRuntime(
            world, updates, fluids, meshes, physicsQueue,
            new MeshletContentRevisions(),
            new MeshletContentRevisions());
        var drops = new DroppedBlockRuntime(world, blocks);
        var runtime = new BlockPhysicsRuntime(
            world, blocks, mutations, physicsQueue, drops);
        var support = new WorldVoxelCoord(4, 8, 3);
        var plant = new WorldVoxelCoord(4, 8, 4);
        Assert.True(mutations.SetBlockAt(support,
            blocks.GetId("asteria:stone"), out _));
        Assert.True(mutations.SetCellAt(plant,
            new VoxelCell(blocks.GetId("asteria:moss_wall"),
                facing:HorizontalFacing.South), out _));

        runtime.ProcessWakeups();
        Assert.False(world.GetCellOrEmpty(plant).IsEmpty);

        Assert.True(mutations.SetCellAt(support, VoxelCell.Empty, out _));
        var result = runtime.ProcessWakeups();
        Assert.Equal(1, result.UnsupportedRemoved);
        Assert.True(world.GetCellOrEmpty(plant).IsEmpty);
        Assert.Single(drops.ActiveBlocks);
    }

    [Theory]
    [InlineData(HorizontalFacing.North, 0, 1)]
    [InlineData(HorizontalFacing.South, 0, -1)]
    [InlineData(HorizontalFacing.East, -1, 0)]
    [InlineData(HorizontalFacing.West, 1, 0)]
    public void WallSupportUsesTheStoredOutwardNormal(
        HorizontalFacing facing, int supportDx, int supportDz)
    {
        var world = new VoxelWorld();
        var chunk = new Chunk();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:wall_plant",
                tags: [BlockPhysicsCapabilities.SupportWall],
                isCollidable:false)
        ]);
        var plant = new WorldVoxelCoord(5, 8, 5);
        var support = plant + (supportDx, 0, supportDz);
        var plantCell = new VoxelCell(
            blocks.GetId("asteria:wall_plant"), facing: facing);
        chunk.SetCell(plant.X, plant.Y, plant.Z, plantCell);
        chunk.SetBlock(support.X, support.Y, support.Z,
            blocks.GetId("asteria:stone"));
        var definition = blocks.GetDefinition(plantCell.Block);
        Assert.Equal(BlockSupportState.Supported,
            BlockSupportRules.Evaluate(world, blocks, definition, plantCell,
                world.GetMicroblockMaskOrEmpty(plant), plant));
        chunk.SetBlock(support.X, support.Y, support.Z, BlockRuntimeId.Air);
        Assert.Equal(BlockSupportState.Unsupported,
            BlockSupportRules.Evaluate(world, blocks, definition, plantCell,
                world.GetMicroblockMaskOrEmpty(plant), plant));
    }

    [Fact]
    public void WallSupportWorksAcrossHorizontalChunkBoundary()
    {
        var world = new VoxelWorld();
        var supportChunk = new Chunk();
        var plantChunk = new Chunk();
        world.InsertChunk(ChunkCoord.Zero, supportChunk);
        world.InsertChunk(new ChunkCoord(1, 0, 0), plantChunk);
        var blocks = new BlockRegistry([
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:wall_plant",
                tags: [BlockPhysicsCapabilities.SupportWall],
                isCollidable:false)
        ]);
        var plantPosition = new WorldVoxelCoord(Chunk.Size, 8, 4);
        var plant = new VoxelCell(
            blocks.GetId("asteria:wall_plant"),
            facing: HorizontalFacing.East);
        plantChunk.SetCell(0, 8, 4, plant);
        supportChunk.SetBlock(Chunk.Size - 1, 8, 4,
            blocks.GetId("asteria:stone"));

        var definition = blocks.GetDefinition(plant.Block);
        Assert.Equal(BlockSupportState.Supported,
            BlockSupportRules.Evaluate(world, blocks, definition, plant,
                world.GetMicroblockMaskOrEmpty(plantPosition),
                plantPosition));
        supportChunk.SetBlock(Chunk.Size - 1, 8, 4, BlockRuntimeId.Air);
        Assert.Equal(BlockSupportState.Unsupported,
            BlockSupportRules.Evaluate(world, blocks, definition, plant,
                world.GetMicroblockMaskOrEmpty(plantPosition),
                plantPosition));
    }

    private static Fixture CreateFixture(
        int maximumActive = 2048)
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:sand",
                    tags:
                    [
                        BlockPhysicsCapabilities.Gravity,
                    ]),
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var worldUpdates =
            new WorldUpdateQueue();
        var fluidUpdates =
            new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var terrainRevisions =
            new MeshletContentRevisions();
        var fluidRevisions =
            new MeshletContentRevisions();
        var mutations =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                terrainRevisions,
                fluidRevisions);
        var dropped =
            new DroppedBlockRuntime(
                world,
                blocks);
        var physics =
            new BlockPhysicsRuntime(
                world,
                blocks,
                mutations,
                physicsUpdates,
                dropped,
                maximumActive);

        return new Fixture(
            world,
            blocks,
            mutations,
            physics,
            dropped,
            worldUpdates,
            fluidUpdates,
            fluidMeshUpdates,
            physicsUpdates,
            fluidRevisions);
    }

    private sealed record Fixture(
        VoxelWorld World,
        BlockRegistry Blocks,
        VoxelMutationRuntime Mutations,
        BlockPhysicsRuntime Physics,
        DroppedBlockRuntime Dropped,
        WorldUpdateQueue WorldUpdates,
        FluidUpdateQueue FluidUpdates,
        FluidMeshUpdateQueue FluidMeshUpdates,
        BlockPhysicsUpdateQueue PhysicsUpdates,
        MeshletContentRevisions FluidRevisions);
}
