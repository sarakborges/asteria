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
            worldUpdates,
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
        WorldUpdateQueue WorldUpdates,
        ChunkResidencyRuntime Runtime);
}
