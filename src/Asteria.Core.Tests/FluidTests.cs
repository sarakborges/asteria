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
        Assert.Equal(
            (ushort)7,
            forward.MaximumSpread);
    }

    [Fact]
    public void VerticalFallResetsHorizontalDistance()
    {
        var fluids = CreateFluids();
        var water =
            fluids.GetId("asteria:water");
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
            FluidCell.Spreading(
                water,
                7,
                1),
            FluidSimulationSolver.HorizontalSpread(
                source,
                7));

        Assert.Equal(
            FluidCell.Spreading(
                water,
                1,
                7),
            FluidSimulationSolver.HorizontalSpread(
                FluidCell.Spreading(
                    water,
                    2,
                    6),
                7));
    }

    [Fact]
    public void HorizontalFlowPrefersNearestReachableDrop()
    {
        var fluids = CreateFluids();
        var water =
            fluids.GetId("asteria:water");
        var world =
            WorldWithFloor();
        var origin =
            new WorldVoxelCoord(6, 1, 6);
        var east =
            origin + (1, 0, 0);
        var north =
            origin + (0, 0, -1);

        world.SetFluidAt(
            origin,
            FluidCell.Source(water),
            out _);
        world.SetBlockAt(
            new WorldVoxelCoord(8, 0, 6),
            BlockRuntimeId.Air,
            out _);

        var eastDesired =
            FluidSimulationSolver.DesiredFluid(
                world,
                fluids,
                east,
                FluidCell.Empty);
        var northDesired =
            FluidSimulationSolver.DesiredFluid(
                world,
                fluids,
                north,
                FluidCell.Empty);

        Assert.False(eastDesired.IsEmpty);
        Assert.True(northDesired.IsEmpty);
    }

    [Fact]
    public void HorizontalFlowSpreadsNormallyWithoutReachableDrop()
    {
        var fluids = CreateFluids();
        var water =
            fluids.GetId("asteria:water");
        var world =
            WorldWithFloor();
        var origin =
            new WorldVoxelCoord(6, 1, 6);

        world.SetFluidAt(
            origin,
            FluidCell.Source(water),
            out _);

        foreach (var offset in new[]
                 {
                     (1, 0, 0),
                     (-1, 0, 0),
                     (0, 0, 1),
                     (0, 0, -1),
                 })
        {
            Assert.False(
                FluidSimulationSolver.DesiredFluid(
                    world,
                    fluids,
                    origin + offset,
                    FluidCell.Empty).IsEmpty);
        }
    }

    [Fact]
    public void ScheduledTickDoesNotSpreadBeforeItsDueWorldTick()
    {
        var fluids = CreateFluids();
        var water =
            fluids.GetId("asteria:water");
        var world =
            WorldWithFloor();
        var source =
            new WorldVoxelCoord(8, 1, 8);
        var target =
            source + (1, 0, 0);
        var queue =
            new FluidUpdateQueue();

        world.SetFluidAt(
            source,
            FluidCell.Source(water),
            out _);

        queue.ScheduleAt(
            new FluidTickKey(
                water,
                target),
            dueTick: 8);

        Assert.False(
            queue.HasReadyWork(7));
        Assert.True(
            queue.DrainReady(
                7,
                512).IsEmpty);

        var due =
            queue.DrainReady(
                8,
                512);
        var result =
            FluidSimulationSolver.Process(
                world.CloneFluidNeighborhood(
                    due.Positions,
                    fluids.MaximumSpread),
                fluids,
                due);

        Assert.Single(result.Changes);
        Assert.Equal(
            target,
            result.Changes[0].Position);
        Assert.False(
            result.Changes[0].Current.IsEmpty);
        Assert.Contains(
            result.ScheduleRequests,
            request =>
                request.Neighborhood &&
                request.Fluid == water &&
                request.Position == target);
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

    private static VoxelWorld WorldWithFloor()
    {
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
        return world;
    }

    private static FluidRegistry CreateFluids() =>
        FluidRegistry.FromJson(
        [
            """
            {
              "id": "asteria:water",
              "color": "4f9fd6",
              "opacity": 0.72,
              "spreadSpeed": 5.0,
              "maxSpread": 7
            }
            """
        ]);
}

public sealed class FluidTimingTests
{
    [Theory]
    [InlineData(16f, 40u, 3ul)]
    [InlineData(4f, 40u, 10ul)]
    [InlineData(80f, 40u, 1ul)]
    public void SpreadSpeedQuantizesToWorldTickDelay(
        float spreadSpeed,
        uint ticksPerSecond,
        ulong expected)
    {
        Assert.Equal(
            expected,
            FluidTiming.DelayTicks(
                spreadSpeed,
                ticksPerSecond));
    }

    [Fact]
    public void ZeroSpreadSpeedDisablesScheduling()
    {
        Assert.Null(
            FluidTiming.DelayTicks(
                0f,
                40));
    }

    [Fact]
    public void WorldTickClockIsIndependentFromFrameGranularity()
    {
        var clock = new WorldTickClock();

        clock.Advance(0.010, 40);
        Assert.Equal(
            0ul,
            clock.CurrentTick);

        clock.Advance(0.015, 40);
        Assert.Equal(
            1ul,
            clock.CurrentTick);

        clock.Advance(0.050, 40);
        Assert.Equal(
            3ul,
            clock.CurrentTick);
    }
}

public sealed class FluidUpdateQueueTests
{
    [Fact]
    public void ScheduledTickKeepsEarliestDueTime()
    {
        var queue = new FluidUpdateQueue();
        var key =
            new FluidTickKey(
                new FluidRuntimeId(1),
                new WorldVoxelCoord(4, 5, 6));

        queue.ScheduleAt(key, 10);
        queue.ScheduleAt(key, 12);
        queue.ScheduleAt(key, 8);

        Assert.True(
            queue.DrainReady(
                7,
                10).IsEmpty);

        var due =
            queue.DrainReady(
                8,
                10);

        Assert.Equal(
            new[] { key },
            due.DueTicks);
    }

    [Fact]
    public void EqualDueTicksRoundRobinBetweenChunks()
    {
        var queue = new FluidUpdateQueue();
        var water = new FluidRuntimeId(1);
        var a1 =
            new FluidTickKey(
                water,
                new WorldVoxelCoord(1, 2, 3));
        var a2 =
            new FluidTickKey(
                water,
                new WorldVoxelCoord(2, 2, 3));
        var b1 =
            new FluidTickKey(
                water,
                new WorldVoxelCoord(17, 2, 3));
        var b2 =
            new FluidTickKey(
                water,
                new WorldVoxelCoord(18, 2, 3));

        foreach (var key in new[]
                 {
                     a1,
                     a2,
                     b1,
                     b2,
                 })
        {
            queue.ScheduleAt(
                key,
                10);
        }

        var batch =
            queue.DrainReady(
                10,
                4);

        Assert.Equal(
            new[] { a1, b1, a2, b2 },
            batch.DueTicks);
    }

    [Fact]
    public void DormantTickReactivatesWhenChunkReturns()
    {
        var queue = new FluidUpdateQueue();
        var key =
            new FluidTickKey(
                new FluidRuntimeId(1),
                new WorldVoxelCoord(17, 2, 3));

        queue.DeferUnloaded(key);
        Assert.Equal(
            1,
            queue.DormantChunkCount);

        queue.ReactivateLoadedChunk(
            new ChunkCoord(1, 0, 0),
            currentTick: 14);

        var batch =
            queue.DrainReady(
                14,
                10);

        Assert.Equal(
            new[] { key },
            batch.DueTicks);
        Assert.Equal(
            0,
            queue.DormantChunkCount);
    }
}
