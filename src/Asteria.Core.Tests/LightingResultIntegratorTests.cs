using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class LightingResultIntegratorTests
{
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
