using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FluidLightingTests
{
    [Theory]
    [InlineData(15, 1, 2)]
    [InlineData(15, 4, 8)]
    [InlineData(15, 8, 15)]
    public void FluidDampeningScalesWithFillLevel(
        byte fullDampening,
        byte level,
        byte expected)
    {
        Assert.Equal(
            expected,
            VoxelLightingMedium.ScaleFluidDampening(
                fullDampening,
                level));
    }

    [Fact]
    public void FullWaterLayerReducesDirectSkyBelowIt()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            CreateWater(
                lightDampening: 1);
        var water =
            fluids.GetId("asteria:water");
        var chunk = new Chunk();

        for (var z = 0;
             z < Chunk.Size;
             z++)
        {
            for (var x = 0;
                 x < Chunk.Size;
                 x++)
            {
                chunk.SetFluid(
                    x,
                    10,
                    z,
                    FluidCell.Source(water));
            }
        }

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            fluids);

        Assert.Equal(
            (byte)14,
            chunk.GetLight(
                8,
                10,
                8).Sky);
        Assert.Equal(
            (byte)14,
            chunk.GetLight(
                8,
                9,
                8).Sky);
    }

    [Fact]
    public void BlockLightAttenuatesThroughFluidMedium()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:red_lamp",
                    lightDampening: 15,
                    lightEmission:
                        new BlockLightEmission(
                            15,
                            0,
                            0)),
            ]);
        var lamp =
            blocks.GetId("asteria:red_lamp");
        var fluids =
            CreateWater(
                lightDampening: 3);
        var water =
            fluids.GetId("asteria:water");
        var chunk = new Chunk();

        chunk.SetBlock(
            7,
            8,
            7,
            lamp);
        chunk.SetFluid(
            8,
            8,
            7,
            FluidCell.Source(water));

        ChunkLightingSolver.Initialize(
            chunk,
            blocks,
            fluids);

        Assert.Equal(
            (byte)12,
            chunk.GetLight(
                8,
                8,
                7).Red);
    }

    [Fact]
    public void IncrementalFluidPlacementAndRemovalRelightsWithoutFullReset()
    {
        var blocks =
            new BlockRegistry([]);
        var fluids =
            CreateWater(
                lightDampening: 1);
        var water =
            fluids.GetId("asteria:water");
        var world =
            new VoxelWorld();

        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        VoxelWorldLightingSolver.Initialize(
            world,
            blocks,
            fluids);

        var position =
            new WorldVoxelCoord(
                8,
                10,
                8);

        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(
                position).Sky);

        world.SetFluidAt(
            position,
            FluidCell.Source(water),
            out _);

        var darken =
            VoxelWorldLightingSolver.RelightAfterEdits(
                world,
                blocks,
                fluids,
                [position]);

        Assert.Equal(
            (byte)14,
            world.GetLightOrDark(
                position).Sky);
        Assert.Contains(
            position,
            darken.ChangedPositions);

        world.SetFluidAt(
            position,
            FluidCell.Empty,
            out _);

        var brighten =
            VoxelWorldLightingSolver.RelightAfterEdits(
                world,
                blocks,
                fluids,
                [position]);

        Assert.Equal(
            (byte)15,
            world.GetLightOrDark(
                position).Sky);
        Assert.Contains(
            position,
            brighten.ChangedPositions);
    }

    private static FluidRegistry CreateWater(
        byte lightDampening) =>
        new(
        [
            new FluidDefinition(
                "asteria:water",
                new FluidColor(
                    79,
                    159,
                    214),
                opacity: 0.72f,
                lightDampening:
                    lightDampening,
                spreadSpeed: 5f,
                maxSpread: 7),
        ]);
}
