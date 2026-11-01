using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class LightingMeshSchedulingTests
{
    [Fact]
    public void LightingSlicesPublishOnlyRequestedVoxelRange()
    {
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, new Chunk());
        var original = new WorldVoxelCoord(2, 2, 2);
        var later = new WorldVoxelCoord(3, 3, 3);
        var snapshot = world.CloneForWorker();
        snapshot.TrySetLight(original, new VoxelLight(0, 15, 0, 0));
        snapshot.TrySetLight(later, new VoxelLight(0, 0, 15, 0));
        var integrator = new LightingResultIntegrator(
            world, new WorldUpdateQueue(), new FluidMeshUpdateQueue());
        var positions = new[] { original, later };

        var first = integrator.ApplyRange(snapshot, positions, 0, 1);
        Assert.Equal(1, first.ChangedVoxelCount);
        Assert.Equal((byte)15, world.GetLightOrDark(original).Red);
        Assert.Equal((byte)0, world.GetLightOrDark(later).Green);
        var second = integrator.ApplyRange(snapshot, positions, 1, 1);
        Assert.Equal(1, second.ChangedVoxelCount);
        Assert.Equal((byte)15, world.GetLightOrDark(later).Green);
    }

    [Fact]
    public void DeferredLightingRefreshCoalescesRepeatedMeshletInvalidations()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        var chunk = new Chunk();
        chunk.SetBlock(2, 2, 2, blocks.GetId("asteria:stone"));
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var terrain = new WorldUpdateQueue();
        var fluid = new FluidMeshUpdateQueue();
        var integrator = new LightingResultIntegrator(world, terrain, fluid);
        var positions = new[]
        {
            new WorldVoxelCoord(2, 2, 2),
            new WorldVoxelCoord(3, 2, 2),
        };
        var snapshot = world.CloneForWorker();
        snapshot.TrySetLight(positions[0], new VoxelLight(0, 15, 0, 0));
        snapshot.TrySetLight(positions[1], new VoxelLight(0, 0, 15, 0));

        integrator.AccumulateRange(snapshot, positions, 0, 1);
        integrator.AccumulateRange(snapshot, positions, 1, 1);
        Assert.False(terrain.HasMeshWork);
        Assert.False(fluid.HasWork);
        Assert.Equal((byte)15, world.GetLightOrDark(positions[0]).Red);
        Assert.Equal((byte)15, world.GetLightOrDark(positions[1]).Green);

        var applied = integrator.FlushAccumulated();
        Assert.Equal(1, applied.DirtyChunkCount);
        Assert.Equal(1, applied.DirtyMeshletCount);
        Assert.True(terrain.HasBackgroundMeshWork);
        Assert.False(fluid.HasWork);
        Assert.Equal(1, terrain.DrainBackgroundMeshlets().DirtyMeshlets.Count);

        Assert.Equal(0, integrator.FlushAccumulated().DirtyChunkCount);
        Assert.False(terrain.HasMeshWork);
    }

    [Fact]
    public void DeferredLightingRefreshRetainsDistinctMeshletMasks()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var world = new VoxelWorld();
        var chunk = new Chunk();
        chunk.SetBlock(2, 2, 2, blocks.GetId("asteria:stone"));
        chunk.SetBlock(12, 2, 2, blocks.GetId("asteria:stone"));
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var terrain = new WorldUpdateQueue();
        var integrator = new LightingResultIntegrator(
            world, terrain, new FluidMeshUpdateQueue());
        var positions = new[]
        {
            new WorldVoxelCoord(2, 2, 2),
            new WorldVoxelCoord(12, 2, 2),
        };
        var snapshot = world.CloneForWorker();
        snapshot.TrySetLight(positions[0], new VoxelLight(15, 0, 0, 0));
        snapshot.TrySetLight(positions[1], new VoxelLight(0, 0, 0, 15));

        integrator.AccumulateRange(snapshot, positions, 0, 1);
        integrator.AccumulateRange(snapshot, positions, 1, 1);
        var expected = ChunkMeshletMask.ForWorldPosition(
                ChunkCoord.Zero, positions[0])
            .Union(ChunkMeshletMask.ForWorldPosition(
                ChunkCoord.Zero, positions[1]));
        var published = integrator.FlushAccumulated();
        Assert.Equal(expected.SelectedCount, published.DirtyMeshletCount);
        var batch = terrain.DrainBackgroundMeshlets();
        Assert.Equal(expected, batch.DirtyMeshlets[ChunkCoord.Zero]);
    }

    [Fact]
    public void LightingRefreshDoesNotScheduleFluidMeshForFluidEmptyChunk()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var world =
            new VoxelWorld();
        var chunk =
            new Chunk();

        chunk.SetBlock(
            2,
            2,
            2,
            blocks.GetId(
                "asteria:stone"));
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var terrain =
            new WorldUpdateQueue();
        var fluid =
            new FluidMeshUpdateQueue();
        var integrator =
            new LightingResultIntegrator(
                world,
                terrain,
                fluid);

        integrator.Apply(
            world.CloneForWorker(),
            [
                new WorldVoxelCoord(
                    2,
                    2,
                    2),
            ]);

        Assert.True(
            terrain.HasBackgroundMeshWork);
        Assert.False(
            fluid.HasWork);
    }

    [Fact]
    public void LightingRefreshDoesNotScheduleTerrainMeshForBlockEmptyChunk()
    {
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(
                        64,
                        96,
                        255),
                    opacity: 0.7f),
            ]);
        var world =
            new VoxelWorld();
        var chunk =
            new Chunk();

        chunk.SetFluid(
            2,
            2,
            2,
            FluidCell.Source(
                fluids.GetId(
                    "asteria:water")));
        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var terrain =
            new WorldUpdateQueue();
        var fluid =
            new FluidMeshUpdateQueue();
        var integrator =
            new LightingResultIntegrator(
                world,
                terrain,
                fluid);

        integrator.Apply(
            world.CloneForWorker(),
            [
                new WorldVoxelCoord(
                    2,
                    2,
                    2),
            ]);

        Assert.False(
            terrain.HasBackgroundMeshWork);
        Assert.True(
            fluid.HasWork);
    }
    [Fact]
    public void LightingRefreshSkipsEmptyMeshletsInAnOtherwiseOccupiedChunk()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var chunk = new Chunk();
        chunk.SetBlock(2, 2, 2, blocks.GetId("asteria:stone"));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var terrain = new WorldUpdateQueue();
        var fluid = new FluidMeshUpdateQueue();
        var integrator = new LightingResultIntegrator(world, terrain, fluid);

        var result = integrator.Apply(
            world.CloneForWorker(), [new WorldVoxelCoord(12, 12, 12)]);

        Assert.Equal(0, result.DirtyChunkCount);
        Assert.Equal(0, result.DirtyMeshletCount);
        Assert.False(terrain.HasMeshWork);
        Assert.False(fluid.HasWork);
    }

    [Fact]
    public void LightingRefreshQueuesOnlyOccupiedTerrainAndFluidMeshlets()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var fluids = new FluidRegistry([
            new FluidDefinition("asteria:water",
                new FluidColor(64, 96, 255), opacity: 0.7f),
        ]);
        var chunk = new Chunk();
        chunk.SetBlock(2, 2, 2, blocks.GetId("asteria:stone"));
        chunk.SetFluid(12, 12, 12, FluidCell.Source(fluids.GetId("asteria:water")));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var terrain = new WorldUpdateQueue();
        var fluid = new FluidMeshUpdateQueue();
        var integrator = new LightingResultIntegrator(world, terrain, fluid);

        var result = integrator.Apply(world.CloneForWorker(), [
            new WorldVoxelCoord(2, 2, 2),
            new WorldVoxelCoord(12, 12, 12),
        ]);

        Assert.Equal(1, result.DirtyChunkCount);
        Assert.Equal(2, result.DirtyMeshletCount);
        Assert.Equal(ChunkMeshletMask.Single(0),
            terrain.DrainBackgroundMeshlets().DirtyMeshlets[ChunkCoord.Zero]);
        Assert.Equal(ChunkMeshletMask.Single(7),
            fluid.Drain().DirtyMeshlets[ChunkCoord.Zero]);
    }

    [Fact]
    public void LightingHaloStillSchedulesAdjacentOccupiedMeshlet()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var chunk = new Chunk();
        chunk.SetBlock(8, 2, 2, blocks.GetId("asteria:stone"));
        var world = new VoxelWorld();
        world.InsertChunk(ChunkCoord.Zero, chunk);
        var terrain = new WorldUpdateQueue();
        var integrator = new LightingResultIntegrator(
            world, terrain, new FluidMeshUpdateQueue());

        var result = integrator.Apply(
            world.CloneForWorker(), [new WorldVoxelCoord(7, 2, 2)]);

        Assert.Equal(1, result.DirtyMeshletCount);
        Assert.Equal(ChunkMeshletMask.Single(1),
            terrain.DrainBackgroundMeshlets().DirtyMeshlets[ChunkCoord.Zero]);
    }

}
