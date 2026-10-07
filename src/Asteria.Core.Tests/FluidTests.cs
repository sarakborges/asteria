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
    public void JsonParsesAuthoredFluidTexturePath()
    {
        const string json =
            """
            {
              "id": "asteria:water",
              "color": "4f9fd6",
              "opacity": 0.72,
              "texture": "textures/fluids/water.png"
            }
            """;

        var registry =
            FluidRegistry.FromJson(
                [json]);
        var definition =
            registry.GetDefinition(
                registry.GetId(
                    "asteria:water"));

        Assert.Equal(
            "textures/fluids/water.png",
            definition.Texture);
    }

    [Fact]
    public void InvalidAuthoredFluidTexturePathIsRejected()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(
                        79,
                        159,
                        214),
                    opacity: 0.72f,
                    texture: " textures/fluids/water.png"));
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

        Assert.Equal(
            FluidCell.Spreading(
                water,
                6,
                1),
            FluidSimulationSolver.HorizontalSpread(
                source,
                3));
        Assert.Equal(
            FluidCell.Spreading(
                water,
                3,
                2),
            FluidSimulationSolver.HorizontalSpread(
                FluidCell.Spreading(
                    water,
                    6,
                    1),
                3));
        Assert.Equal(
            FluidCell.Spreading(
                water,
                1,
                3),
            FluidSimulationSolver.HorizontalSpread(
                FluidCell.Spreading(
                    water,
                    3,
                    2),
                3));
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
    public void HorizontalFlowCrossesChunkBoundary()
    {
        var fluids = CreateFluids();
        var water =
            fluids.GetId("asteria:water");
        var world =
            WorldWithTwoChunkFloor();
        var source =
            new WorldVoxelCoord(
                Chunk.Size - 1,
                1,
                8);
        var target =
            source + (1, 0, 0);

        Assert.True(
            world.SetFluidAt(
                source,
                FluidCell.Source(water),
                out _));

        var desired =
            FluidSimulationSolver.DesiredFluid(
                world,
                fluids,
                target,
                FluidCell.Empty);

        Assert.Equal(
            FluidCell.Spreading(
                water,
                7,
                1),
            desired);
    }

    [Fact]
    public void VerticalFallCrossesChunkBoundary()
    {
        var fluids = CreateFluids();
        var water =
            fluids.GetId("asteria:water");
        var world = new VoxelWorld();

        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        world.InsertChunk(
            new ChunkCoord(0, 1, 0),
            new Chunk());

        var above =
            new WorldVoxelCoord(
                4,
                Chunk.Size,
                4);
        var target =
            above + (0, -1, 0);

        Assert.True(
            world.SetFluidAt(
                above,
                FluidCell.Spreading(
                    water,
                    FluidCell.MinLevel,
                    7),
                out _));

        Assert.Equal(
            FluidCell.Spreading(
                water,
                FluidCell.MaxLevel,
                0),
            FluidSimulationSolver.DesiredFluid(
                world,
                fluids,
                target,
                FluidCell.Empty));
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

    private static VoxelWorld WorldWithTwoChunkFloor()
    {
        var world = new VoxelWorld();

        for (var chunkX = 0;
             chunkX <= 1;
             chunkX++)
        {
            var chunk = new Chunk();

            for (var z = 0;
                 z < Chunk.Size;
                 z++)
            {
                for (var x = 0;
                     x < Chunk.Size;
                     x++)
                {
                    chunk.SetBlock(
                        x,
                        0,
                        z,
                        new BlockRuntimeId(1));
                }
            }

            world.InsertChunk(
                new ChunkCoord(
                    chunkX,
                    0,
                    0),
                chunk);
        }

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

public sealed class FluidMotionSolverTests
{
    [Fact]
    public void NearSurfaceAscendAppliesImmediateExitImpulse()
    {
        var motion =
            new FluidMotionDefinition(
                horizontalSpeedMultiplier: 0.45f,
                horizontalAcceleration: 10f,
                sinkSpeed: 0.9f,
                ascendSpeed: 2.4f,
                surfaceExitSpeed: 8f,
                verticalAcceleration: 10f,
                surfaceExitMargin: 0.35f);
        var contact =
            new FluidBodyContact(
                IsImmersed: true,
                EyeSubmerged: false,
                Fluid: new FluidRuntimeId(1),
                SurfaceY: 10f,
                SampleY: 9.8f);

        Assert.Equal(
            8f,
            FluidMotionSolver.VerticalSpeed(
                currentSpeed: 2.4f,
                deltaSeconds: 1f / 60f,
                contact,
                motion,
                ascendRequested: true));
    }

    [Fact]
    public void DeepWaterAscendStillUsesAuthoredAcceleration()
    {
        var motion =
            new FluidMotionDefinition(
                horizontalSpeedMultiplier: 0.45f,
                horizontalAcceleration: 10f,
                sinkSpeed: 0.9f,
                ascendSpeed: 2.4f,
                surfaceExitSpeed: 8f,
                verticalAcceleration: 10f,
                surfaceExitMargin: 0.35f);
        var contact =
            new FluidBodyContact(
                IsImmersed: true,
                EyeSubmerged: true,
                Fluid: new FluidRuntimeId(1),
                SurfaceY: 10f,
                SampleY: 9f);

        Assert.Equal(
            1f,
            FluidMotionSolver.VerticalSpeed(
                currentSpeed: 0f,
                deltaSeconds: 0.1f,
                contact,
                motion,
                ascendRequested: true),
            5);
    }
}

public sealed class FluidMotionDefinitionTests
{
    [Fact]
    public void JsonParsesAuthoredFluidMotion()
    {
        const string json = """
            {
              "id": "asteria:water",
              "color": "4f9fd6",
              "opacity": 0.72,
              "motion": {
                "horizontalSpeedMultiplier": 0.4,
                "horizontalAcceleration": 8.5,
                "sinkSpeed": 1.1,
                "ascendSpeed": 2.6,
                "surfaceExitSpeed": 4.8,
                "verticalAcceleration": 9.5,
                "surfaceExitMargin": 0.3
              }
            }
            """;

        var registry =
            FluidRegistry.FromJson(
                [json]);
        var motion =
            registry
                .GetDefinition(
                    registry.GetId(
                        "asteria:water"))
                .Motion;

        Assert.Equal(
            0.4f,
            motion.HorizontalSpeedMultiplier);
        Assert.Equal(
            8.5f,
            motion.HorizontalAcceleration);
        Assert.Equal(
            1.1f,
            motion.SinkSpeed);
        Assert.Equal(
            2.6f,
            motion.AscendSpeed);
        Assert.Equal(
            4.8f,
            motion.SurfaceExitSpeed);
        Assert.Equal(
            9.5f,
            motion.VerticalAcceleration);
        Assert.Equal(
            0.3f,
            motion.SurfaceExitMargin);
    }

    [Fact]
    public void MissingMotionUsesDeterministicDefaults()
    {
        var definition =
            new FluidDefinition(
                "asteria:water",
                new FluidColor(
                    79,
                    159,
                    214),
                opacity: 0.72f);

        Assert.Equal(
            FluidMotionDefinition.Default,
            definition.Motion);
        Assert.True(
            definition.Motion.SinkSpeed > 0f);
        Assert.True(
            definition.Motion.HorizontalSpeedMultiplier < 1f);
    }

    [Fact]
    public void NegativeMotionValueIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new FluidMotionDefinition(
                    horizontalSpeedMultiplier: 0.5f,
                    horizontalAcceleration: -1f,
                    sinkSpeed: 1f,
                    ascendSpeed: 2f,
                    surfaceExitSpeed: 5f,
                    verticalAcceleration: 10f,
                    surfaceExitMargin: 0.35f));
    }
}

public sealed class FluidSimulationSnapshotTests
{
    [Fact]
    public void RelevantContentEditInvalidatesSnapshot()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        var snapshot =
            FluidSimulationSnapshot.Capture(
                world,
                new FluidWorkBatch(
                    [new WorldVoxelCoord(8, 8, 8)],
                    Array.Empty<FluidTickKey>()),
                horizontalVoxelRadius: 7);

        Assert.True(
            snapshot.Dependencies.IsCurrent(
                world));

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(4, 4, 4),
                new BlockRuntimeId(1),
                out _));

        Assert.False(
            snapshot.Dependencies.IsCurrent(
                world));
    }

    [Fact]
    public void RelevantChunkResidencyChangeInvalidatesSnapshot()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        var snapshot =
            FluidSimulationSnapshot.Capture(
                world,
                new FluidWorkBatch(
                    [new WorldVoxelCoord(8, 8, 8)],
                    Array.Empty<FluidTickKey>()),
                horizontalVoxelRadius: 7);

        world.InsertChunk(
            new ChunkCoord(0, 1, 0),
            new Chunk());

        Assert.False(
            snapshot.Dependencies.IsCurrent(
                world));
    }

    [Fact]
    public void UnrelatedVerticalResidencyDoesNotInvalidateSnapshot()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());

        var snapshot =
            FluidSimulationSnapshot.Capture(
                world,
                new FluidWorkBatch(
                    [new WorldVoxelCoord(8, 8, 8)],
                    Array.Empty<FluidTickKey>()),
                horizontalVoxelRadius: 7);

        world.InsertChunk(
            new ChunkCoord(0, 2, 0),
            new Chunk());

        Assert.True(
            snapshot.Dependencies.IsCurrent(
                world));
    }

    [Fact]
    public void UnrelatedColumnEditDoesNotInvalidateSnapshot()
    {
        var world = new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        world.InsertChunk(
            new ChunkCoord(3, 0, 0),
            new Chunk());

        var snapshot =
            FluidSimulationSnapshot.Capture(
                world,
                new FluidWorkBatch(
                    [new WorldVoxelCoord(8, 8, 8)],
                    Array.Empty<FluidTickKey>()),
                horizontalVoxelRadius: 7);

        Assert.True(
            world.SetBlockAt(
                new WorldVoxelCoord(
                    3 * Chunk.Size + 1,
                    1,
                    1),
                new BlockRuntimeId(1),
                out _));

        Assert.True(
            snapshot.Dependencies.IsCurrent(
                world));
    }

    [Fact]
    public void SnapshotUsesOnlyDescribedNeighborhoodChunks()
    {
        var world = new VoxelWorld();

        foreach (var coord in new[]
                 {
                     new ChunkCoord(0, 0, 0),
                     new ChunkCoord(0, 1, 0),
                     new ChunkCoord(0, 2, 0),
                     new ChunkCoord(0, 8, 0),
                     new ChunkCoord(1, 0, 0),
                     new ChunkCoord(2, 0, 0),
                 })
        {
            world.InsertChunk(
                coord,
                new Chunk());
        }

        var snapshot =
            FluidSimulationSnapshot.Capture(
                world,
                new FluidWorkBatch(
                    [new WorldVoxelCoord(8, 8, 8)],
                    Array.Empty<FluidTickKey>()),
                horizontalVoxelRadius: 7);

        Assert.True(
            snapshot.World.ContainsChunk(
                new ChunkCoord(0, 0, 0)));
        Assert.True(
            snapshot.World.ContainsChunk(
                new ChunkCoord(0, 1, 0)));
        Assert.True(
            snapshot.World.ContainsChunk(
                new ChunkCoord(1, 0, 0)));
        Assert.False(
            snapshot.World.ContainsChunk(
                new ChunkCoord(0, 2, 0)));
        Assert.False(
            snapshot.World.ContainsChunk(
                new ChunkCoord(0, 8, 0)));
        Assert.False(
            snapshot.World.ContainsChunk(
                new ChunkCoord(2, 0, 0)));
    }
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
    public void TopologyDrainKeepsDeterministicVoxelOrder()
    {
        var queue =
            new FluidUpdateQueue();
        var laterChunk =
            new WorldVoxelCoord(
                17,
                2,
                3);
        var laterVoxel =
            new WorldVoxelCoord(
                2,
                2,
                3);
        var firstVoxel =
            new WorldVoxelCoord(
                1,
                2,
                3);

        queue.EnqueueTopology(laterChunk);
        queue.EnqueueTopology(laterVoxel);
        queue.EnqueueTopology(firstVoxel);
        queue.EnqueueTopology(firstVoxel);

        var batch =
            queue.DrainReady(
                currentTick: 0,
                maximumItems: 10);

        Assert.Equal(
            new[]
            {
                firstVoxel,
                laterVoxel,
                laterChunk,
            },
            batch.TopologyPositions);
        Assert.Equal(
            0,
            queue.TopologyCount);
    }

    [Fact]
    public void TopologyDrainLeavesOrderedBacklogWithoutResorting()
    {
        var queue =
            new FluidUpdateQueue();

        for (var x = 7;
             x >= 0;
             x--)
        {
            queue.EnqueueTopology(
                new WorldVoxelCoord(
                    x,
                    2,
                    3));
        }

        var first =
            queue.DrainReady(
                currentTick: 0,
                maximumItems: 3);
        var second =
            queue.DrainReady(
                currentTick: 0,
                maximumItems: 3);

        Assert.Equal(
            new[]
            {
                new WorldVoxelCoord(0, 2, 3),
                new WorldVoxelCoord(1, 2, 3),
                new WorldVoxelCoord(2, 2, 3),
            },
            first.TopologyPositions);
        Assert.Equal(
            new[]
            {
                new WorldVoxelCoord(3, 2, 3),
                new WorldVoxelCoord(4, 2, 3),
                new WorldVoxelCoord(5, 2, 3),
            },
            second.TopologyPositions);
        Assert.Equal(
            2,
            queue.TopologyCount);
    }

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
