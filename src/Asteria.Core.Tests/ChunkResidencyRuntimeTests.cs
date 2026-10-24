using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkResidencyRuntimeTests
{
    [Fact]
    public void DispatchRestoresArchivedChunkBeforeProviderWork()
    {
        var fixture = CreateFixture();
        var coord = ChunkCoord.Zero;

        fixture.World.InsertChunk(
            coord,
            new Chunk());
        Assert.True(
            fixture.World.SetBlockAt(
                new WorldVoxelCoord(1, 1, 1),
                fixture.Blocks.GetId("asteria:stone"),
                out _));
        Assert.Equal(
            ChunkArchiveResult.ArchivedDirty,
            fixture.World.ArchiveChunk(coord));

        fixture.Runtime.SyncSelection(
            coord,
            horizontalRadius: 1,
            retentionRadius: 2,
            desired: new HashSet<ChunkCoord> { coord },
            presented: Array.Empty<ChunkCoord>());

        var update =
            fixture.Runtime.DispatchMaterializationTasks(
                GenerousBudget(),
                Array.Empty<ChunkCoord>());

        var activation =
            Assert.Single(update.Activations);

        Assert.Equal(coord, activation.Coord);
        Assert.Equal(
            ChunkActivationSource.Archive,
            activation.Source);
        Assert.True(
            fixture.World.ContainsChunk(coord));
        Assert.Equal(
            1,
            fixture.Runtime.PresentationPendingCount);
    }

    [Fact]
    public void PressuredGenerationPrioritizesLocalChunksAndEventuallyDispatchesDistantOnes()
    {
        var fixture = CreateFixture();
        var near = new ChunkCoord(1, 0, 0);
        var far = new ChunkCoord(5, 0, 0);
        foreach (var coord in new[] { near, far })
        {
            fixture.World.InsertChunk(coord, new Chunk());
            fixture.World.ArchiveChunk(coord);
        }

        fixture.Runtime.SyncSelection(ChunkCoord.Zero,
            horizontalRadius: 5, retentionRadius: 6,
            desired: new HashSet<ChunkCoord> { near, far },
            presented: Array.Empty<ChunkCoord>());

        var first = fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>(),
            deferDistantWork: true);
        Assert.Equal(near, Assert.Single(first.Activations).Coord);
        Assert.False(fixture.World.ContainsChunk(far));
        Assert.Equal(1, fixture.Runtime.PendingCount);

        for (var frame = 2; frame < 8; frame++)
            Assert.Empty(fixture.Runtime.DispatchMaterializationTasks(
                GenerousBudget(), Array.Empty<ChunkCoord>(),
                deferDistantWork: true).Activations);

        var eighth = fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>(),
            deferDistantWork: true);
        Assert.Equal(far, Assert.Single(eighth.Activations).Coord);
        Assert.Equal(0, fixture.Runtime.PendingCount);
    }

    [Fact]
    public void UnpressuredGenerationImmediatelyResumesDistantDispatch()
    {
        var fixture = CreateFixture();
        var far = new ChunkCoord(5, 0, 0);
        fixture.World.InsertChunk(far, new Chunk());
        fixture.World.ArchiveChunk(far);
        fixture.Runtime.SyncSelection(ChunkCoord.Zero,
            horizontalRadius: 5, retentionRadius: 6,
            desired: new HashSet<ChunkCoord> { far },
            presented: Array.Empty<ChunkCoord>());

        Assert.Empty(fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>(),
            deferDistantWork: true).Activations);
        var resumed = fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());
        Assert.Equal(far, Assert.Single(resumed.Activations).Coord);
    }

    [Fact]
    public void ArchivedRestoresAreBoundedPerFrameAndRemainPendingForNextFrame()
    {
        var fixture = CreateFixture();
        var coords = new[]
        {
            new ChunkCoord(0, 0, 0),
            new ChunkCoord(1, 0, 0),
            new ChunkCoord(0, 0, 1),
            new ChunkCoord(1, 0, 1),
            new ChunkCoord(0, 1, 0),
        };

        foreach (var coord in coords)
        {
            fixture.World.InsertChunk(coord, new Chunk());
            fixture.World.ArchiveChunk(coord);
        }

        fixture.Runtime.SyncSelection(
            ChunkCoord.Zero,
            horizontalRadius: 1,
            retentionRadius: 2,
            desired: coords.ToHashSet(),
            presented: Array.Empty<ChunkCoord>());

        var first = fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());
        Assert.Equal(2, first.Activations.Count);
        Assert.Equal(3, fixture.Runtime.PendingCount);

        var second = fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());
        Assert.Equal(2, second.Activations.Count);
        Assert.Equal(1, fixture.Runtime.PendingCount);

        var third = fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());
        Assert.Single(third.Activations);
        Assert.Equal(0, fixture.Runtime.PendingCount);
        Assert.Equal(5, fixture.World.ChunkCount);
        Assert.All(
            first.Activations.Concat(second.Activations).Concat(third.Activations),
            activation => Assert.Equal(ChunkActivationSource.Archive, activation.Source));
    }

    [Fact]


    [Fact]
    public void UnloadedSourceFluidFrontierWakesOnlyWhenTargetChunkArrives()
    {
        var fixture = CreateFixture();
        var source = ChunkCoord.Zero;
        var east = new ChunkCoord(1, 0, 0);
        var chunk = new Chunk();
        var stone = fixture.Blocks.GetId("asteria:stone");
        chunk.SetFluid(15, 5, 5,
            FluidCell.Source(fixture.Fluids.GetId("asteria:water")));
        chunk.SetBlock(14, 5, 5, stone);
        chunk.SetBlock(15, 4, 5, stone);
        chunk.SetBlock(15, 5, 4, stone);
        chunk.SetBlock(15, 5, 6, stone);
        fixture.World.InsertChunk(source, chunk);
        fixture.World.ArchiveChunk(source);
        fixture.World.InsertChunk(east, new Chunk());
        fixture.World.ArchiveChunk(east);

        fixture.Runtime.SyncSelection(
            source, horizontalRadius: 1, retentionRadius: 2,
            desired: new HashSet<ChunkCoord> { source },
            presented: Array.Empty<ChunkCoord>());
        fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());
        CompleteFluidActivations(fixture);
        Assert.True(fixture.World.ContainsChunk(source));
        Assert.False(fixture.World.ContainsChunk(east));
        Assert.Equal(0, fixture.FluidUpdates.Count);

        fixture.Runtime.SyncSelection(
            source, horizontalRadius: 1, retentionRadius: 2,
            desired: new HashSet<ChunkCoord> { source, east },
            presented: Array.Empty<ChunkCoord>());
        fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());
        CompleteFluidActivations(fixture);
        Assert.True(fixture.World.ContainsChunk(east));
        Assert.True(fixture.FluidUpdates.ScheduledCount > 0);
    }

    [Fact]
    public void FluidActivationDelayCacheInvalidatesWhenTickRateChanges()
    {
        var fixture = CreateFixture();
        var coord = ChunkCoord.Zero;
        var position = new WorldVoxelCoord(8, 8, 8);
        var chunk = new Chunk();
        chunk.SetFluid(
            8, 8, 8,
            FluidCell.Spreading(
                fixture.Fluids.GetId("asteria:water"),
                level: 4,
                spreadDistance: 1));
        fixture.World.InsertChunk(coord, chunk);
        fixture.World.ArchiveChunk(coord);

        fixture.Runtime.SyncSelection(
            coord,
            horizontalRadius: 1,
            retentionRadius: 2,
            desired: new HashSet<ChunkCoord> { coord },
            presented: Array.Empty<ChunkCoord>());
        fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());
        CompleteFluidActivations(fixture);

        var firstDue = Assert.Single(
            fixture.FluidUpdates.CaptureState().Scheduled,
            scheduled => scheduled.Tick.Position == position);
        Assert.Equal(40UL, firstDue.DueTick);
        fixture.FluidUpdates.DrainReady(40, maximumItems: 128);

        Assert.NotEqual(
            ChunkArchiveResult.NotResident,
            fixture.World.ArchiveChunk(coord));
        fixture.Runtime.SyncResidentState(Array.Empty<ChunkCoord>());
        Assert.True(fixture.Rules.SetTicksPerSecond(80));

        fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());
        CompleteFluidActivations(fixture);

        var secondDue = Assert.Single(
            fixture.FluidUpdates.CaptureState().Scheduled,
            scheduled => scheduled.Tick.Position == position);
        Assert.Equal(80UL, secondDue.DueTick);
    }

    [Fact]
    public void FluidActivationIsBoundedPerFrameAndEventuallyCompletesEveryChunk()
    {
        var fixture = CreateFixture();
        var water = fixture.Fluids.GetId("asteria:water");
        var coords = new[]
        {
            ChunkCoord.Zero,
            new ChunkCoord(1, 0, 0),
        };

        foreach (var coord in coords)
        {
            var chunk = new Chunk();
            for (var index = 0; index < 1024; index++)
            {
                var x = index % Chunk.Size;
                var z = index / Chunk.Size % Chunk.Size;
                var y = index / Chunk.Area;
                chunk.SetFluid(
                    x, y, z,
                    FluidCell.Spreading(water, level: 4, spreadDistance: 1));
            }

            fixture.World.InsertChunk(coord, chunk);
            fixture.World.ArchiveChunk(coord);
        }

        fixture.Runtime.SyncSelection(
            ChunkCoord.Zero,
            horizontalRadius: 1,
            retentionRadius: 2,
            desired: coords.ToHashSet(),
            presented: Array.Empty<ChunkCoord>());
        var restored = fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());
        Assert.Equal(2, restored.Activations.Count);

        var first = fixture.Runtime.ProcessPendingFluidActivations(GenerousBudget());
        Assert.InRange(first.SlicesProcessed, 1, 8);
        Assert.InRange(
            first.FluidCellsProcessed +
            first.FrontierCellsProcessed +
            first.DormantTicksReactivated,
            1,
            first.SlicesProcessed * 32);
        Assert.True(first.FluidCellsProcessed <= 256);
        Assert.Equal(2, first.PendingChunks);

        var totalFluidCells = first.FluidCellsProcessed;
        var totalFrontierCells = first.FrontierCellsProcessed;
        var totalDormantTicks = first.DormantTicksReactivated;
        var totalCompletedChunks = first.CompletedChunks;
        var report = first;
        for (var frame = 0; frame < 100 && report.PendingChunks > 0; frame++)
        {
            report = fixture.Runtime.ProcessPendingFluidActivations(GenerousBudget());
            Assert.True(report.FluidCellsProcessed <= 256);
            Assert.InRange(
                report.FluidCellsProcessed +
                report.FrontierCellsProcessed +
                report.DormantTicksReactivated,
                0,
                report.SlicesProcessed * 32);
            totalFluidCells += report.FluidCellsProcessed;
            totalFrontierCells += report.FrontierCellsProcessed;
            totalDormantTicks += report.DormantTicksReactivated;
            totalCompletedChunks += report.CompletedChunks;
        }

        Assert.Equal(0, report.PendingChunks);
        Assert.Equal(2, totalCompletedChunks);
        Assert.Equal(2048, totalFluidCells);
        Assert.True(totalFrontierCells > 0);
        Assert.Equal(0, totalDormantTicks);
    }

    [Fact]
    public void DormantFluidTicksReactivateInBoundedBatches()
    {
        var fixture = CreateFixture();
        var coord = ChunkCoord.Zero;
        var water = fixture.Fluids.GetId("asteria:water");
        for (var index = 0; index < 512; index++)
        {
            var position = new WorldVoxelCoord(
                index % Chunk.Size,
                index / Chunk.Area,
                index / Chunk.Size % Chunk.Size);
            fixture.FluidUpdates.DeferUnloaded(
                new FluidTickKey(water, position));
        }

        fixture.World.InsertChunk(coord, new Chunk());
        fixture.World.ArchiveChunk(coord);
        fixture.Runtime.SyncSelection(
            coord,
            horizontalRadius: 1,
            retentionRadius: 2,
            desired: new HashSet<ChunkCoord> { coord },
            presented: Array.Empty<ChunkCoord>());
        fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());

        var first = fixture.Runtime.ProcessPendingFluidActivations(GenerousBudget());
        Assert.Equal(256, first.DormantTicksReactivated);
        Assert.Equal(256, fixture.FluidUpdates.ScheduledCount);
        Assert.Equal(1, fixture.FluidUpdates.DormantChunkCount);
        Assert.Equal(1, first.PendingChunks);

        CompleteFluidActivations(fixture);
        Assert.Equal(512, fixture.FluidUpdates.ScheduledCount);
        Assert.Equal(0, fixture.FluidUpdates.DormantChunkCount);
    }

    [Fact]


    [Fact]
    public void PresentationDoesNotWaitForUnmaterializedNeighbor()
    {
        var fixture = CreateFixture();
        var center = ChunkCoord.Zero;
        var neighbor = new ChunkCoord(1, 0, 0);

        fixture.World.InsertChunk(center, new Chunk());
        fixture.Runtime.SyncSelection(
            center,
            horizontalRadius: 1,
            retentionRadius: 2,
            desired: new HashSet<ChunkCoord> { center, neighbor },
            presented: Array.Empty<ChunkCoord>());

        // The local chunk must be immediately presentable even when
        // the desired dependency halo has not been materialized.
        Assert.Equal(center, fixture.Runtime.PopPresentationByPriority());
        Assert.Null(fixture.Runtime.PopPresentationByPriority());

        fixture.World.InsertChunk(neighbor, new Chunk());
        fixture.Runtime.SyncResidentState(new[] { center });
        Assert.Equal(neighbor, fixture.Runtime.PopPresentationByPriority());
    }

    [Fact]
    public void RetirementArchivesDirtyChunkAndClearsResidency()
    {
        var fixture = CreateFixture();
        var origin = ChunkCoord.Zero;

        fixture.World.InsertChunk(
            origin,
            new Chunk());
        Assert.True(
            fixture.World.SetBlockAt(
                new WorldVoxelCoord(1, 1, 1),
                fixture.Blocks.GetId("asteria:stone"),
                out _));

        fixture.Runtime.SyncSelection(
            origin,
            horizontalRadius: 1,
            retentionRadius: 1,
            desired: new HashSet<ChunkCoord> { origin },
            presented: Array.Empty<ChunkCoord>());

        var far = new ChunkCoord(10, 0, 0);

        fixture.Runtime.SyncSelection(
            far,
            horizontalRadius: 1,
            retentionRadius: 1,
            desired: new HashSet<ChunkCoord> { far },
            presented: Array.Empty<ChunkCoord>());

        var retired =
            fixture.Runtime.RetireDistantChunks(
                GenerousBudget());
        var retirement =
            Assert.Single(retired);

        Assert.Equal(origin, retirement.Coord);
        Assert.Equal(
            ChunkArchiveResult.ArchivedDirty,
            retirement.ArchiveResult);
        Assert.False(
            fixture.World.ContainsChunk(origin));
        Assert.Equal(
            1,
            fixture.World.ArchivedChunkCount);
    }

    private static WorldFrameWorkBudget GenerousBudget() =>
        new(long.MaxValue);

    private static void CompleteFluidActivations(RuntimeFixture fixture)
    {
        var report = fixture.Runtime.ProcessPendingFluidActivations(GenerousBudget());
        for (var frame = 0; report.PendingChunks > 0 && frame < 100; frame++)
        {
            report = fixture.Runtime.ProcessPendingFluidActivations(GenerousBudget());
        }

        Assert.Equal(0, report.PendingChunks);
    }

    private static RuntimeFixture CreateFixture(IChunkProvider? chunkProvider = null)
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition("asteria:block_grass"),
                new BlockDefinition("asteria:dirt"),
                new BlockDefinition("asteria:stone"),
                new BlockDefinition("asteria:sand"),
                new BlockDefinition("asteria:gravel"),
                new BlockDefinition("asteria:clay"),
                new BlockDefinition("asteria:mud"),
            ]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(79, 159, 214),
                    opacity: 0.72f),
            ]);

        var world = new VoxelWorld();
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
        var ticks = new WorldTickClock();
        var rules = new WorldGameRules();
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
                dropped);
        var runtime =
            new ChunkResidencyRuntime(
                world,
                blocks,
                fluids,
                chunkProvider ?? new EmptyChunkProvider(),
                worldUpdates,
                fluidUpdates,
                fluidMeshUpdates,
                physics,
                terrainRevisions,
                fluidRevisions,
                ticks,
                new ChunkResidencySettings(
                    maxMaterializationsInFlight: 2,
                    maxDispatchesPerFrame: 2,
                    maxResultsPerFrame: 2,
                    maxEvictionsPerFrame: 2,
                    gameRules: rules));

        return new RuntimeFixture(
            world,
            blocks,
            fluids,
            worldUpdates,
            fluidUpdates,
            runtime,
            rules);
    }

    private sealed class EmptyChunkProvider : IChunkProvider
    {
        public Chunk Materialize(
            ChunkCoord coord) =>
            new();
    }

    private sealed class SignalingChunkProvider : IChunkProvider, IDisposable
    {
        public ManualResetEventSlim Materialized { get; } = new(false);

        public Chunk Materialize(ChunkCoord coord)
        {
            var chunk = new Chunk();
            Materialized.Set();
            return chunk;
        }

        public void Dispose() => Materialized.Dispose();
    }

    private sealed record RuntimeFixture(
        VoxelWorld World,
        BlockRegistry Blocks,
        FluidRegistry Fluids,
        WorldUpdateQueue WorldUpdates,
        FluidUpdateQueue FluidUpdates,
        ChunkResidencyRuntime Runtime,
        WorldGameRules Rules);
}
