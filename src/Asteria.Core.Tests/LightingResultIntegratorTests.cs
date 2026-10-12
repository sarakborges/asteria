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
}
