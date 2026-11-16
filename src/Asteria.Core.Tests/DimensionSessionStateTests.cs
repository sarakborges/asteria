using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DimensionSessionStateTests
{
    [Fact]
    public void RecreateKeepsSeedSettingsAndCurrentRulesWithoutWorldData()
    {
        var dimensions = new DimensionRegistry(
            [Dimension("asteria:overworld")]);
        var generation = new WorldGenerationOptions(
            WorldGenerationMode.Flat, biomeSizeTenths: 16,
            spawnStructures: false, spawnCaves: false, spawnOceans: false);
        var old = new DimensionSessionStateStore(
            new WorldCreationOptions("Test World", ulong.MaxValue,
                PlayerGameMode.Creative, 25, false, generation,
                false, CreatureAttackMode.Peaceful),
            dimensions);
        old.GameRules.SetTicksPerSecond(42);
        old.GameRules.SetSpawnCreatures(true);
        old.GameRules.SetCreatureAttackMode(CreatureAttackMode.Default);
        var copy = old.CreateRecreationOptions();

        Assert.Equal("Copy of Test World", copy.Name);
        Assert.Equal(ulong.MaxValue, copy.Seed);
        Assert.Equal(PlayerGameMode.Creative, copy.GameMode);
        Assert.Equal(42u, copy.TicksPerSecond);
        Assert.True(copy.SpawnCreatures);
        Assert.False(copy.KeepInventory);
        Assert.Equal(CreatureAttackMode.Default, copy.AttackMode);
        Assert.Same(generation, copy.Generation);
        var fresh = new DimensionSessionStateStore(copy, dimensions);
        var id = new DimensionId("asteria:overworld");
        Assert.NotSame(old.GetOrCreate(id).World, fresh.GetOrCreate(id).World);
    }

    [Fact]
    public void RecreateKeepsCopyNameWithinFilesystemLimit()
    {
        var dimensions = new DimensionRegistry(
            [Dimension("asteria:overworld")]);
        var name = new string('A', WorldCreationOptions.MaximumNameLength);
        var world = new DimensionSessionStateStore(
            new WorldCreationOptions(name, 77UL), dimensions);
        var copy = world.CreateRecreationOptions();
        Assert.Equal(WorldCreationOptions.MaximumNameLength, copy.Name.Length);
        Assert.StartsWith("Copy of ", copy.Name);
        Assert.Equal(77UL, copy.Seed);
    }

    [Fact]
    public void DimensionStatesKeepWorldsAndPositionsIsolated()
    {
        var dimensions =
            new DimensionRegistry(
            [
                Dimension(
                    "asteria:overworld"),
                Dimension(
                    "asteria:umbral"),
            ]);
        var states =
            new DimensionSessionStateStore(
                worldSeed: 42,
                dimensions);
        var overworld =
            states.GetOrCreate(
                new DimensionId(
                    "asteria:overworld"));
        var umbral =
            states.GetOrCreate(
                new DimensionId(
                    "asteria:umbral"));

        Assert.NotSame(
            overworld.World,
            umbral.World);
        Assert.NotEqual(
            overworld.DimensionSeed,
            umbral.DimensionSeed);

        overworld.PlayerPosition =
            new Vector3(
                12.5f,
                7f,
                -4.5f);
        umbral.PlayerPosition =
            new Vector3(
                -30f,
                5f,
                18f);

        Assert.Equal(
            new Vector3(
                12.5f,
                7f,
                -4.5f),
            states.GetOrCreate(
                    new DimensionId(
                        "asteria:overworld"))
                .PlayerPosition);
        Assert.Equal(
            new Vector3(
                -30f,
                5f,
                18f),
            states.GetOrCreate(
                    new DimensionId(
                        "asteria:umbral"))
                .PlayerPosition);
    }

    [Fact]
    public void StorageBoxesStayWithTheirSphereAcrossChunkArchival()
    {
        var dimensions = new DimensionRegistry(
        [
            Dimension("asteria:overworld"),
            Dimension("asteria:umbral"),
        ]);
        var states = new DimensionSessionStateStore(77UL, dimensions);
        var overworld = states.GetOrCreate(
            new DimensionId("asteria:overworld"));
        var umbral = states.GetOrCreate(
            new DimensionId("asteria:umbral"));
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:box_storage"),
        ]);
        var position = new WorldVoxelCoord(3, 6, 3);
        overworld.World.InsertChunk(ChunkCoord.Zero, new Chunk());
        Assert.True(overworld.World.SetBlockAt(position,
            blocks.GetId("asteria:box_storage"), out _));
        Assert.True(overworld.StorageBoxes.TryOpen(
            position, overworld.World, blocks));
        Assert.True(overworld.StorageBoxes.TryInsertActive(
            new InventoryStack(InventoryEntry.FromItem("asteria:wood"), 5),
            overworld.World, blocks));
        overworld.StorageBoxes.Close();

        overworld.ArchiveResidentWorld();
        Assert.Equal(ChunkRestoreResult.Restored,
            overworld.World.RestoreChunk(ChunkCoord.Zero));

        var restored = states.GetOrCreate(new DimensionId("asteria:overworld"));
        Assert.Same(overworld.StorageBoxes, restored.StorageBoxes);
        var stored = Assert.Single(restored.StorageBoxes.CaptureOccupied());
        Assert.Equal(position, stored.Position);
        Assert.Equal(5, stored.Slots[0]!.Quantity);
        Assert.Empty(umbral.StorageBoxes.CaptureOccupied());
    }

    [Fact]
    public void RetiredDimensionArchivesDirtyAndPristineMaterializedChunks()
    {
        var dimensions =
            new DimensionRegistry(
            [
                Dimension(
                    "asteria:overworld"),
            ]);
        var state =
            new DimensionSessionStateStore(
                worldSeed: 42,
                dimensions)
                .GetOrCreate(
                    new DimensionId(
                        "asteria:overworld"));
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var stone =
            blocks.GetId(
                "asteria:stone");
        var edited =
            new Chunk();
        var pristine =
            new Chunk();
        var editedCoord =
            ChunkCoord.Zero;
        var pristineCoord =
            new ChunkCoord(
                1,
                0,
                0);

        state.World.InsertChunk(
            editedCoord,
            edited);
        state.World.InsertChunk(
            pristineCoord,
            pristine);

        edited.SetBlock(
            2,
            3,
            4,
            stone);

        var report =
            state.ArchiveResidentWorld();

        Assert.Equal(
            1,
            report.ArchivedDirty);
        Assert.Equal(
            1,
            report.ArchivedPristine);
        Assert.Equal(
            0,
            state.World.ChunkCount);
        Assert.Equal(
            2,
            state.World.ArchivedChunkCount);
        Assert.Equal(
            1,
            state.World.DirtyChunkCount);
        Assert.True(
            state.World.HasArchivedChunk(
                editedCoord));
        Assert.True(
            state.World.HasArchivedChunk(
                pristineCoord));

        Assert.Equal(
            ChunkRestoreResult.Restored,
            state.World.RestoreChunk(
                editedCoord));
        Assert.Equal(
            stone,
            state.World.GetChunk(
                    editedCoord)
                .GetBlock(
                    2,
                    3,
                    4));

        Assert.Equal(
            ChunkRestoreResult.Restored,
            state.World.RestoreChunk(
                pristineCoord));
        Assert.True(
            state.World.GetChunk(
                    pristineCoord)
                .GetBlock(
                    0,
                    0,
                    0)
                .IsAir);
    }

    [Fact]
    public void WorldTickClockResumesAtSavedSessionTick()
    {
        var clock =
            new WorldTickClock(
                initialTick: 1234);

        Assert.Equal(
            1234UL,
            clock.CurrentTick);

        clock.Advance(
            0.05,
            ticksPerSecond: 40);

        Assert.Equal(
            1236UL,
            clock.CurrentTick);
    }

    [Fact]
    public void FallingAndDroppedRuntimeSnapshotsRoundTrip()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:sand",
                    tags:
                    [
                        BlockPhysicsCapabilities.Gravity,
                    ]),
            ]);
        var world =
            new VoxelWorld();
        world.InsertChunk(
            ChunkCoord.Zero,
            new Chunk());
        var worldUpdates =
            new WorldUpdateQueue();
        var fluidUpdates =
            new FluidUpdateQueue();
        var fluidMeshUpdates =
            new FluidMeshUpdateQueue();
        var physicsUpdates =
            new BlockPhysicsUpdateQueue();
        var terrainRevisions =
            new MeshletContentRevisions();
        var fluidRevisions =
            new MeshletContentRevisions();
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
        var sand =
            blocks.GetId(
                "asteria:sand");
        var position =
            new WorldVoxelCoord(
                4,
                8,
                4);

        Assert.True(
            mutations.SetBlockAt(
                position,
                sand,
                out _));
        physicsUpdates.Enqueue(
            position);
        Assert.Equal(
            1,
            physics.ProcessWakeups()
                .FallingStarted);

        dropped.Spawn(
            new BlockStateSnapshot(
                new VoxelCell(sand),
                MicroblockMask.Empty),
            new Vector3(
                6.5f,
                5.5f,
                6.5f));

        var fallingSnapshot =
            physics.CaptureState();
        var fallingActiveCount =
            physics.ActiveCount;
        var droppedSnapshot =
            dropped.CaptureState();
        var droppedActiveCount =
            dropped.ActiveCount;

        // Only one authoritative physics runtime may be attached to a
        // mutation owner for a Sphere at a time.
        physics.Dispose();

        var restoredDropped =
            new DroppedBlockRuntime(
                world,
                blocks,
                restore:
                    droppedSnapshot);
        using var restoredPhysics =
            new BlockPhysicsRuntime(
                world,
                blocks,
                mutations,
                new BlockPhysicsUpdateQueue(),
                restoredDropped,
                restore:
                    fallingSnapshot);

        Assert.Equal(
            fallingActiveCount,
            restoredPhysics.ActiveCount);
        Assert.Equal(
            droppedActiveCount,
            restoredDropped.ActiveCount);
        var restoredFallingSnapshot =
            restoredPhysics.CaptureState();
        var restoredDroppedSnapshot =
            restoredDropped.CaptureState();

        Assert.Equal(
            fallingSnapshot.NextId,
            restoredFallingSnapshot.NextId);
        Assert.Equal(
            fallingSnapshot.ActiveBlocks.ToArray(),
            restoredFallingSnapshot.ActiveBlocks.ToArray());
        Assert.Equal(
            droppedSnapshot.NextId,
            restoredDroppedSnapshot.NextId);
        Assert.Equal(
            droppedSnapshot.ActiveBlocks.ToArray(),
            restoredDroppedSnapshot.ActiveBlocks.ToArray());
    }

    private static DimensionDefinition Dimension(
        string id) =>
        new(
            new DimensionId(
                id),
            [
                id +
                "/plain",
            ],
            seaLevel: 90,
            18f,
            new DimensionSpawnDefinition(
                0,
                0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(
                    0,
                    0,
                    0),
                new DimensionColor(
                    255,
                    255,
                    255),
                1f,
                new DimensionColor(
                    0,
                    0,
                    0),
                0f));
}
