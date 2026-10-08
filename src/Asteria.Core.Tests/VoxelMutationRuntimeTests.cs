using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VoxelMutationRuntimeTests
{
    [Fact]
    public void BlockEditQueuesTerrainFluidLightingAndFluidTopologyTogether()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());

        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates = new FluidMeshUpdateQueue();
        var blockPhysicsUpdates = new BlockPhysicsUpdateQueue();
        var terrainRevisions = new MeshletContentRevisions();
        var fluidRevisions = new MeshletContentRevisions();
        var runtime = new VoxelMutationRuntime(
            world,
            worldUpdates,
            fluidUpdates,
            fluidMeshUpdates,
            blockPhysicsUpdates,
            terrainRevisions,
            fluidRevisions);
        var position = new WorldVoxelCoord(3, 3, 3);
        var key = new ChunkMeshletKey(ChunkCoord.Zero, 0);

        Assert.True(runtime.SetBlockAt(
            position,
            new BlockRuntimeId(1),
            out var edit));

        Assert.Equal(position, edit.Position);
        Assert.True(worldUpdates.HasLightingWork);
        Assert.True(worldUpdates.HasMeshWork);
        Assert.Equal(7, fluidUpdates.TopologyCount);
        Assert.True(fluidMeshUpdates.HasWork);
        Assert.Equal(2, blockPhysicsUpdates.Count);
        Assert.True(terrainRevisions.Get(key) > 0);
        Assert.True(fluidRevisions.Get(key) > 0);
    }

    [Fact]
    public void FluidEditUsesSameMutationBoundaryAndWakesRequiredSystems()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates = new FluidMeshUpdateQueue();
        var blockPhysicsUpdates = new BlockPhysicsUpdateQueue();
        var terrainRevisions = new MeshletContentRevisions();
        var fluidRevisions = new MeshletContentRevisions();
        var runtime = new VoxelMutationRuntime(
            world,
            worldUpdates,
            fluidUpdates,
            fluidMeshUpdates,
            blockPhysicsUpdates,
            terrainRevisions,
            fluidRevisions);
        var position =
            new WorldVoxelCoord(3, 3, 3);
        var key =
            new ChunkMeshletKey(
                ChunkCoord.Zero,
                0);
        var water =
            new FluidRuntimeId(1);

        Assert.True(
            runtime.SetFluidAt(
                position,
                FluidCell.Source(water),
                out var edit));

        Assert.Equal(position, edit.Position);
        Assert.True(worldUpdates.HasLightingWork);
        Assert.False(worldUpdates.HasMeshWork);
        Assert.Equal(7, fluidUpdates.TopologyCount);
        Assert.True(fluidMeshUpdates.HasWork);
        Assert.Equal(0, blockPhysicsUpdates.Count);
        Assert.Equal(0UL, terrainRevisions.Get(key));
        Assert.True(fluidRevisions.Get(key) > 0);
    }

    [Fact]
    public void PlacingBlockDisplacesFluidThroughDerivedFluidConsequences()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        var position =
            new WorldVoxelCoord(3, 3, 3);
        var water =
            new FluidRuntimeId(1);

        Assert.True(
            world.SetFluidAt(
                position,
                FluidCell.Source(water),
                out _));

        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates = new FluidMeshUpdateQueue();
        var blockPhysicsUpdates = new BlockPhysicsUpdateQueue();
        var terrainRevisions = new MeshletContentRevisions();
        var fluidRevisions = new MeshletContentRevisions();
        var runtime = new VoxelMutationRuntime(
            world,
            worldUpdates,
            fluidUpdates,
            fluidMeshUpdates,
            blockPhysicsUpdates,
            terrainRevisions,
            fluidRevisions);

        Assert.True(
            runtime.SetBlockAt(
                position,
                new BlockRuntimeId(1),
                out _));

        Assert.True(
            world.GetFluidOrEmpty(position).IsEmpty);
        Assert.True(fluidMeshUpdates.HasWork);
        Assert.True(worldUpdates.HasLightingWork);
        Assert.Equal(7, fluidUpdates.TopologyCount);
    }

    [Fact]
    public void BlockReplacingFluidPublishesFluidConsequencesExactlyOnce()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var position =
            new WorldVoxelCoord(3, 3, 3);
        var water =
            new FluidRuntimeId(1);

        Assert.True(
            world.SetFluidAt(
                position,
                FluidCell.Source(water),
                out _));

        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var terrainRevisions =
            new MeshletContentRevisions();
        var fluidRevisions =
            new MeshletContentRevisions();
        var runtime =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                terrainRevisions,
                fluidRevisions);
        var key =
            new ChunkMeshletKey(
                ChunkCoord.Zero,
                0);

        Assert.Equal(
            0UL,
            fluidRevisions.Get(key));

        Assert.True(
            runtime.SetBlockAt(
                position,
                new BlockRuntimeId(1),
                out _));

        Assert.Equal(
            1UL,
            fluidRevisions.Get(key));
        Assert.Equal(
            7,
            fluidUpdates.TopologyCount);
        Assert.True(
            world.GetFluidOrEmpty(
                    position)
                .IsEmpty);
    }

    [Fact]
    public void PortableBlockReplacingFluidPublishesConsequencesExactlyOnce()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var position =
            new WorldVoxelCoord(3, 3, 3);
        var water =
            new FluidRuntimeId(1);

        Assert.True(
            world.SetFluidAt(
                position,
                FluidCell.Source(water),
                out _));

        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var terrainRevisions =
            new MeshletContentRevisions();
        var fluidRevisions =
            new MeshletContentRevisions();
        var runtime =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                terrainRevisions,
                fluidRevisions);
        var key =
            new ChunkMeshletKey(
                ChunkCoord.Zero,
                0);

        Assert.True(
            runtime.SetBlockStateAt(
                position,
                BlockStateSnapshot.FromCell(
                    new VoxelCell(
                        new BlockRuntimeId(1))),
                out _));

        Assert.Equal(
            1UL,
            fluidRevisions.Get(key));
        Assert.Equal(
            7,
            fluidUpdates.TopologyCount);
        Assert.True(
            world.GetFluidOrEmpty(
                    position)
                .IsEmpty);
    }

    [Fact]
    public void BoundaryBlockEditInvalidatesFluidHaloAcrossChunksOnce()
    {
        var world = new VoxelWorld();
        var left =
            ChunkCoord.Zero;
        var right =
            new ChunkCoord(1, 0, 0);

        world.InsertChunk(
            left,
            new Chunk());
        world.InsertChunk(
            right,
            new Chunk());

        var position =
            new WorldVoxelCoord(
                Chunk.Size - 1,
                3,
                3);
        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var terrainRevisions =
            new MeshletContentRevisions();
        var fluidRevisions =
            new MeshletContentRevisions();
        var runtime =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                terrainRevisions,
                fluidRevisions);

        Assert.True(
            runtime.SetBlockAt(
                position,
                new BlockRuntimeId(1),
                out _));

        foreach (var coord in new[] { left, right })
        {
            var mask =
                ChunkMeshletMask.ForWorldPosition(
                    coord,
                    position);

            Assert.False(mask.IsEmpty);

            foreach (var meshletIndex in
                     mask.Indices())
            {
                Assert.Equal(
                    1UL,
                    fluidRevisions.Get(
                        new ChunkMeshletKey(
                            coord,
                            meshletIndex)));
            }
        }

        var dirty =
            fluidMeshUpdates.Drain();

        Assert.Contains(
            left,
            dirty.DirtyMeshlets.Keys);
        Assert.Contains(
            right,
            dirty.DirtyMeshlets.Keys);

        var topology =
            fluidUpdates.DrainReady(
                currentTick: 0,
                maximumItems: 32);

        Assert.Contains(
            new WorldVoxelCoord(
                Chunk.Size,
                3,
                3),
            topology.TopologyPositions);
    }

    [Fact]
    public void RemovingBlockImmediatelyWakesFluidTopologyForAuthoredDelay()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var target =
            new WorldVoxelCoord(3, 3, 3);
        var above =
            target + (0, 1, 0);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(79, 159, 214),
                    opacity: 0.72f,
                    spreadSpeed: 4f),
            ]);
        var water =
            fluids.GetId("asteria:water");

        Assert.True(
            world.SetBlockAt(
                target,
                new BlockRuntimeId(1),
                out _));
        Assert.True(
            world.SetFluidAt(
                above,
                FluidCell.Source(water),
                out _));

        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var runtime =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physicsUpdates,
                new MeshletContentRevisions(),
                new MeshletContentRevisions());

        Assert.True(
            runtime.SetBlockAt(
                target,
                BlockRuntimeId.Air,
                out _));

        var batch =
            fluidUpdates.DrainReady(
                currentTick: 0,
                maximumItems: 32);
        var result =
            FluidSimulationSolver.Process(
                world,
                fluids,
                batch);

        Assert.Contains(
            result.ScheduleRequests,
            request =>
                request.Fluid == water &&
                request.Position == target &&
                !request.Neighborhood);
        Assert.True(
            world.GetFluidOrEmpty(
                    target)
                .IsEmpty);
    }

    [Fact]
    public void FluidBatchCoalescesRevisionAndMeshInvalidationPerMeshlet()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var fluidRevisions =
            new MeshletContentRevisions();
        var runtime =
            new VoxelMutationRuntime(
                world,
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                new BlockPhysicsUpdateQueue(),
                new MeshletContentRevisions(),
                fluidRevisions);
        var water =
            new FluidRuntimeId(1);
        var first =
            new WorldVoxelCoord(2, 2, 2);
        var second =
            new WorldVoxelCoord(3, 2, 2);
        var key =
            new ChunkMeshletKey(
                ChunkCoord.Zero,
                0);

        var result =
            runtime.ApplyFluidChanges(
            [
                new FluidCellChange(
                    first,
                    FluidCell.Empty,
                    FluidCell.Source(water)),
                new FluidCellChange(
                    second,
                    FluidCell.Empty,
                    FluidCell.Source(water)),
            ]);

        Assert.True(result.Accepted);
        Assert.Equal(2, result.AppliedChangeCount);
        Assert.Equal(2, result.UniquePositionCount);
        Assert.Equal(
            1UL,
            fluidRevisions.Get(key));

        var dirty =
            fluidMeshUpdates.Drain();

        Assert.True(
            dirty.DirtyMeshlets[
                ChunkCoord.Zero]
                .ContainsIndex(0));
        Assert.True(
            worldUpdates.HasLightingWork);
    }

    [Fact]
    public void FluidBatchAllowsSequentialTransitionsAtSameVoxel()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var fluidRevisions =
            new MeshletContentRevisions();
        var runtime =
            new VoxelMutationRuntime(
                world,
                new WorldUpdateQueue(),
                new FluidUpdateQueue(),
                new FluidMeshUpdateQueue(),
                new BlockPhysicsUpdateQueue(),
                new MeshletContentRevisions(),
                fluidRevisions);
        var position =
            new WorldVoxelCoord(2, 2, 2);
        var water =
            new FluidRuntimeId(1);
        var spreading =
            FluidCell.Spreading(
                water,
                4,
                2);

        var result =
            runtime.ApplyFluidChanges(
            [
                new FluidCellChange(
                    position,
                    FluidCell.Empty,
                    spreading),
                new FluidCellChange(
                    position,
                    spreading,
                    FluidCell.Source(water)),
            ]);

        Assert.True(result.Accepted);
        Assert.Equal(2, result.AppliedChangeCount);
        Assert.Equal(1, result.UniquePositionCount);
        Assert.Equal(
            FluidCell.Source(water),
            world.GetFluidOrEmpty(
                position));
        Assert.Equal(
            1UL,
            fluidRevisions.Get(
                new ChunkMeshletKey(
                    ChunkCoord.Zero,
                    0)));
    }

    [Fact]
    public void StaleFluidBatchRejectsBeforeApplyingAnyChange()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var water =
            new FluidRuntimeId(1);
        var stale =
            new WorldVoxelCoord(2, 2, 2);
        var untouched =
            new WorldVoxelCoord(3, 2, 2);

        Assert.True(
            world.SetFluidAt(
                stale,
                FluidCell.Source(water),
                out _));

        var fluidRevisions =
            new MeshletContentRevisions();
        var runtime =
            new VoxelMutationRuntime(
                world,
                new WorldUpdateQueue(),
                new FluidUpdateQueue(),
                new FluidMeshUpdateQueue(),
                new BlockPhysicsUpdateQueue(),
                new MeshletContentRevisions(),
                fluidRevisions);

        var result =
            runtime.ApplyFluidChanges(
            [
                new FluidCellChange(
                    stale,
                    FluidCell.Empty,
                    FluidCell.Spreading(
                        water,
                        7,
                        1)),
                new FluidCellChange(
                    untouched,
                    FluidCell.Empty,
                    FluidCell.Source(water)),
            ]);

        Assert.False(result.Accepted);
        Assert.True(
            world.GetFluidOrEmpty(
                    untouched)
                .IsEmpty);
        Assert.Equal(
            0UL,
            fluidRevisions.Get(
                new ChunkMeshletKey(
                    ChunkCoord.Zero,
                    0)));
    }

    [Fact]
    public void StructureBatchRejectsMissingChunkWithoutPublishingAnyEdits()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var updates = new WorldUpdateQueue();
        var fluids = new FluidUpdateQueue();
        var meshes = new FluidMeshUpdateQueue();
        var physics = new BlockPhysicsUpdateQueue();
        var runtime = new VoxelMutationRuntime(
            world, updates, fluids, meshes, physics,
            new MeshletContentRevisions(), new MeshletContentRevisions());
        var resident = new WorldVoxelCoord(3, 4, 3);
        var missing = new WorldVoxelCoord(40, 4, 3);
        var marker = BlockStateSnapshot.FromCell(
            new VoxelCell(new BlockRuntimeId(1)));
        var beforeRevision = world.Revision;

        Assert.False(runtime.ApplyStructureChanges(
        [
            new VoxelStructureChange(resident, marker, FluidCell.Empty),
            new VoxelStructureChange(missing, marker, FluidCell.Empty),
        ]));
        Assert.Equal(beforeRevision, world.Revision);
        Assert.True(world.GetCellOrEmpty(resident).IsEmpty);
        Assert.False(updates.HasWork);
        Assert.False(meshes.HasWork);
        Assert.Equal(0, physics.Count);
    }

    [Fact]
    public void StructureBatchPublishesCompleteBlockFluidAndClearChanges()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var blocked = new WorldVoxelCoord(3, 3, 3);
        var fluid = new WorldVoxelCoord(4, 3, 3);
        var clear = new WorldVoxelCoord(5, 3, 3);
        Assert.True(world.SetBlockAt(
            clear, new BlockRuntimeId(1), out _));
        var updates = new WorldUpdateQueue();
        var runtime = new VoxelMutationRuntime(
            world, updates, new FluidUpdateQueue(),
            new FluidMeshUpdateQueue(), new BlockPhysicsUpdateQueue(),
            new MeshletContentRevisions(), new MeshletContentRevisions());

        Assert.True(runtime.ApplyStructureChanges(
        [
            new VoxelStructureChange(
                blocked, BlockStateSnapshot.FromCell(
                    new VoxelCell(new BlockRuntimeId(1))), FluidCell.Empty),
            new VoxelStructureChange(
                fluid, null, FluidCell.Source(new FluidRuntimeId(1))),
            new VoxelStructureChange(clear, null, FluidCell.Empty),
        ]));
        Assert.False(world.GetCellOrEmpty(blocked).IsEmpty);
        Assert.True(world.GetCellOrEmpty(clear).IsEmpty);
        Assert.Equal(FluidCell.Source(new FluidRuntimeId(1)),
            world.GetFluidOrEmpty(fluid));
        Assert.True(updates.HasWork);
    }

    [Fact]
    public void NoOpBlockEditDoesNotWakeDerivedSystemsAgain()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());

        var worldUpdates = new WorldUpdateQueue();
        var fluidUpdates = new FluidUpdateQueue();
        var fluidMeshUpdates = new FluidMeshUpdateQueue();
        var blockPhysicsUpdates = new BlockPhysicsUpdateQueue();
        var terrainRevisions = new MeshletContentRevisions();
        var fluidRevisions = new MeshletContentRevisions();
        var runtime = new VoxelMutationRuntime(
            world,
            worldUpdates,
            fluidUpdates,
            fluidMeshUpdates,
            blockPhysicsUpdates,
            terrainRevisions,
            fluidRevisions);
        var position = new WorldVoxelCoord(3, 3, 3);
        var block = new BlockRuntimeId(1);
        var key = new ChunkMeshletKey(ChunkCoord.Zero, 0);

        Assert.True(runtime.SetBlockAt(position, block, out _));
        worldUpdates.DrainLighting();
        worldUpdates.DrainMeshlets();
        fluidUpdates.DrainReady(0, 32);
        fluidMeshUpdates.Drain();
        blockPhysicsUpdates.DrainBatch();
        var terrainRevision = terrainRevisions.Get(key);
        var fluidRevision = fluidRevisions.Get(key);

        Assert.False(runtime.SetBlockAt(position, block, out _));

        Assert.False(worldUpdates.HasWork);
        Assert.False(fluidUpdates.HasReadyWork(0));
        Assert.False(fluidMeshUpdates.HasWork);
        Assert.Equal(0, blockPhysicsUpdates.Count);
        Assert.Equal(terrainRevision, terrainRevisions.Get(key));
        Assert.Equal(fluidRevision, fluidRevisions.Get(key));
    }
}
