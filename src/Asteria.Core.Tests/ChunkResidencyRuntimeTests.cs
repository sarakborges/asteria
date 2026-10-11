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
        // Without resident neighbors, the archived chunk already has
        // its own light state and no cross-chunk reconciliation is needed.
        Assert.False(
            fixture.WorldUpdates.HasLightingWork);
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
    public void RestoringChunkWithResidentNeighborQueuesLightingFrontier()
    {
        var fixture = CreateFixture();
        var coord = ChunkCoord.Zero;
        var neighbor = new ChunkCoord(1, 0, 0);
        fixture.World.InsertChunk(coord, new Chunk());
        fixture.World.InsertChunk(neighbor, new Chunk());
        fixture.World.ArchiveChunk(coord);

        fixture.Runtime.SyncSelection(
            coord,
            horizontalRadius: 1,
            retentionRadius: 2,
            desired: new HashSet<ChunkCoord> { coord, neighbor },
            presented: Array.Empty<ChunkCoord>());
        var update = fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(),
            Array.Empty<ChunkCoord>());

        Assert.Equal(
            ChunkActivationSource.Archive,
            Assert.Single(update.Activations).Source);
        Assert.True(fixture.WorldUpdates.HasLightingWork);
    }

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
        Assert.True(fixture.World.ContainsChunk(source));
        Assert.False(fixture.World.ContainsChunk(east));
        Assert.Equal(0, fixture.FluidUpdates.Count);

        fixture.Runtime.SyncSelection(
            source, horizontalRadius: 1, retentionRadius: 2,
            desired: new HashSet<ChunkCoord> { source, east },
            presented: Array.Empty<ChunkCoord>());
        fixture.Runtime.DispatchMaterializationTasks(
            GenerousBudget(), Array.Empty<ChunkCoord>());
        Assert.True(fixture.World.ContainsChunk(east));
        Assert.True(fixture.FluidUpdates.ScheduledCount > 0);
    }

    [Fact]
    public void PresentationWaitsForDesiredDependencyHalo()
    {
        var fixture =
            CreateFixture();
        var center =
            ChunkCoord.Zero;
        var neighbor =
            new ChunkCoord(
                1,
                0,
                0);

        fixture.World.InsertChunk(
            center,
            new Chunk());
        fixture.Runtime.SyncSelection(
            center,
            horizontalRadius: 1,
            retentionRadius: 2,
            desired:
                new HashSet<ChunkCoord>
                {
                    center,
                    neighbor,
                },
            presented:
                Array.Empty<ChunkCoord>());

        Assert.Null(
            fixture.Runtime
                .PopPresentationByPriority());

        fixture.World.InsertChunk(
            neighbor,
            new Chunk());
        fixture.Runtime.SyncResidentState(
            Array.Empty<ChunkCoord>());

        Assert.Equal(
            center,
            fixture.Runtime
                .PopPresentationByPriority());
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

    private static RuntimeFixture CreateFixture()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition("asteria:grass_block"),
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
                new EmptyChunkProvider(),
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
                    gameRules: new WorldGameRules()));

        return new RuntimeFixture(
            world,
            blocks,
            fluids,
            worldUpdates,
            fluidUpdates,
            runtime);
    }

    private sealed class EmptyChunkProvider : IChunkProvider
    {
        public Chunk Materialize(
            ChunkCoord coord) =>
            new();
    }

    private sealed record RuntimeFixture(
        VoxelWorld World,
        BlockRegistry Blocks,
        FluidRegistry Fluids,
        WorldUpdateQueue WorldUpdates,
        FluidUpdateQueue FluidUpdates,
        ChunkResidencyRuntime Runtime);
}
