using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FluidTests
{
    [Fact]
    public void FluidHeightUsesFullEightLevelRange()
    {
        var water = new FluidRuntimeId(1);

        Assert.Equal(
            1f,
            FluidCell.Source(water).Height);
        Assert.Equal(
            0.125f,
            FluidCell.Spreading(
                water,
                FluidCell.MinLevel,
                7).Height);
    }

    [Fact]
    public void RegistryRuntimeIdsAreDeterministic()
    {
        const string water =
            """
            {
              "id": "asteria:water",
              "color": "4f9fd6",
              "opacity": 0.72,
              "maxSpread": 7
            }
            """;

        const string lava =
            """
            {
              "id": "asteria:lava",
              "color": "ff6a21",
              "opacity": 0.9,
              "maxSpread": 3
            }
            """;

        var forward =
            FluidRegistry.FromJson(
                [water, lava]);
        var reverse =
            FluidRegistry.FromJson(
                [lava, water]);

        Assert.Equal(
            forward.GetId("asteria:lava"),
            reverse.GetId("asteria:lava"));
        Assert.Equal(
            forward.GetId("asteria:water"),
            reverse.GetId("asteria:water"));
    }

    [Fact]
    public void VerticalFallResetsHorizontalDistance()
    {
        var fluids = CreateFluids();
        var water = fluids.GetId("asteria:water");
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        var above =
            new WorldVoxelCoord(4, 8, 4);
        world.SetFluidAt(
            above,
            FluidCell.Spreading(
                water,
                FluidCell.MinLevel,
                7),
            out _);

        var target =
            above + (0, -1, 0);
        var desired =
            FluidSimulationSolver.DesiredFluid(
                world,
                fluids,
                target,
                FluidCell.Empty);

        Assert.Equal(
            FluidCell.Spreading(
                water,
                FluidCell.MaxLevel,
                0),
            desired);
    }

    [Fact]
    public void HorizontalSpreadMapsRangeAcrossAllFluidLevels()
    {
        var water = new FluidRuntimeId(1);
        var source = FluidCell.Source(water);

        Assert.Equal(
            FluidCell.Spreading(water, 7, 1),
            FluidSimulationSolver.HorizontalSpread(
                source,
                7));

        Assert.Equal(
            FluidCell.Spreading(water, 1, 7),
            FluidSimulationSolver.HorizontalSpread(
                FluidCell.Spreading(
                    water,
                    2,
                    6),
                7));
    }

    [Fact]
    public void SourceSpreadsAndDynamicCellsDisappearAfterSourceRemoval()
    {
        var fluids = CreateFluids();
        var water = fluids.GetId("asteria:water");
        var world = new VoxelWorld();
        var chunk = new Chunk();

        for (var z = 0; z < Chunk.Size; z++)
        {
            for (var x = 0; x < Chunk.Size; x++)
            {
                chunk.SetBlock(
                    x,
                    0,
                    z,
                    new BlockRuntimeId(1));
            }
        }

        world.InsertChunk(
            ChunkCoord.Zero,
            chunk);

        var source =
            new WorldVoxelCoord(8, 1, 8);
        world.SetFluidAt(
            source,
            FluidCell.Source(water),
            out _);

        var spread =
            FluidSimulationSolver.Process(
                world,
                fluids,
                [
                    source + (1, 0, 0),
                    source + (-1, 0, 0),
                    source + (0, 0, 1),
                    source + (0, 0, -1),
                ],
                4096);

        Assert.NotEmpty(spread.Changes);
        Assert.False(
            world.GetFluidOrEmpty(
                source + (1, 0, 0)).IsEmpty);

        world.SetFluidAt(
            source,
            FluidCell.Empty,
            out _);

        var settle =
            FluidSimulationSolver.Process(
                world,
                fluids,
                [
                    source,
                    source + (1, 0, 0),
                    source + (-1, 0, 0),
                    source + (0, 0, 1),
                    source + (0, 0, -1),
                ],
                8192);

        Assert.NotEmpty(settle.Changes);
        Assert.True(
            world.GetFluidOrEmpty(
                source + (1, 0, 0)).IsEmpty);
    }

    [Fact]
    public void WorkerCloneKeepsFluidStateIndependent()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        var position =
            new WorldVoxelCoord(2, 3, 4);
        world.SetFluidAt(
            position,
            FluidCell.Source(
                new FluidRuntimeId(1)),
            out _);

        var clone =
            world.CloneForWorker();
        clone.SetFluidAt(
            position,
            FluidCell.Empty,
            out _);

        Assert.False(
            world.GetFluidOrEmpty(
                position).IsEmpty);
    }

    private static FluidRegistry CreateFluids() =>
        FluidRegistry.FromJson(
        [
            """
            {
              "id": "asteria:water",
              "color": "4f9fd6",
              "opacity": 0.72,
              "maxSpread": 7
            }
            """
        ]);
}
