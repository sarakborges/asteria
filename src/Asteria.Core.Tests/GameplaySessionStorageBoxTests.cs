using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class GameplaySessionStorageBoxTests
{
    private static DimensionDefinition Dimension(string id) => new(
        new DimensionId(id), [id + "/plain"], 90, 18f,
        new DimensionSpawnDefinition(0, 0),
        new DimensionEnvironmentDefinition(
            new DimensionColor(0, 0, 0),
            new DimensionColor(255, 255, 255), 1f,
            new DimensionColor(0, 0, 0), 0f));

    private static DimensionRegistry Dimensions() => new([
        Dimension("asteria:overworld"), Dimension("asteria:umbral")
    ]);

    private static (BlockRegistry Blocks, FluidRegistry Fluids,
        DyeRegistry Dyes, AttachedLayerRegistry Layers) Content() => (
        new BlockRegistry([
            new BlockDefinition("asteria:storage_box"),
            new BlockDefinition("asteria:stone")
        ]),
        new FluidRegistry([]), new DyeRegistry([]),
        new AttachedLayerRegistry([])
    );

    private static GameplaySessionSnapshot Capture(
        DimensionSessionStateStore store,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        GameplaySessionSaveCodec.Capture(store,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);

    private static DimensionSessionStateStore Restore(
        WorldCreationOptions creation,
        GameplaySessionSnapshot saved,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        GameplaySessionSaveCodec.Restore(creation, Dimensions(), saved,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);

    [Fact]
    public void RestoresStorageContentsInsideResidentAndArchivedSpheres()
    {
        var content = Content();
        var creation = new WorldCreationOptions("World", 4321UL);
        var states = new DimensionSessionStateStore(creation, Dimensions());
        var overworld = states.GetOrCreate(DimensionId.Overworld);
        var umbral = states.GetOrCreate(new DimensionId("asteria:umbral"));
        var pos = new WorldVoxelCoord(2, 4, 3);

        Fill(overworld, pos, "asteria:stick", 11, content.Blocks);
        Fill(umbral, pos, "asteria:pebble", 23, content.Blocks);
        Assert.Equal(ChunkArchiveResult.ArchivedDirty,
            umbral.World.ArchiveChunk(ChunkCoord.Zero));

        var saved = Capture(states, content);
        var sphere = Assert.Single(saved.Spheres.Where(x =>
            x.Dimension == new DimensionId("asteria:umbral")));
        var detached = Assert.Single(sphere.StorageBoxes).Slots;
        detached[0] = null;
        Assert.Equal(23, sphere.StorageBoxes[0].Slots[0]!.Quantity);

        var loaded = Restore(creation, saved, content);
        var first = loaded.GetOrCreate(DimensionId.Overworld);
        var second = loaded.GetOrCreate(new DimensionId("asteria:umbral"));
        Assert.Equal(11, Assert.Single(first.StorageBoxes.CaptureOccupied())
            .Slots[0]!.Quantity);
        Assert.Equal("asteria:stick", first.StorageBoxes.CaptureOccupied()[0]
            .Slots[0]!.Id);
        Assert.Equal(23, Assert.Single(second.StorageBoxes.CaptureOccupied())
            .Slots[0]!.Quantity);
        Assert.Equal("asteria:pebble", second.StorageBoxes.CaptureOccupied()[0]
            .Slots[0]!.Id);
        Assert.Equal(0, second.World.ChunkCount);
        Assert.True(second.World.HasArchivedChunk(ChunkCoord.Zero));
        Assert.Equal(ChunkRestoreResult.Restored,
            second.World.RestoreChunk(ChunkCoord.Zero));
        Assert.True(second.StorageBoxes.TryOpen(pos, second.World, content.Blocks));
        Assert.Equal(23, second.StorageBoxes.CaptureActive()!.Slots[0]!.Quantity);
    }

    [Fact]
    public void RejectsContentsWithoutMatchingStorageBlock()
    {
        var content = Content();
        var creation = new WorldCreationOptions("World", 42UL);
        var states = new DimensionSessionStateStore(creation, Dimensions());
        var sphere = states.GetOrCreate(DimensionId.Overworld);
        sphere.World.InsertChunk(ChunkCoord.Zero, new Chunk());
        var saved = Capture(states, content);
        var bad = new SphereClockSnapshot(
            DimensionId.Overworld, 0, null, null, 0,
            [new SavedStorageBoxContents(
                new WorldVoxelCoord(1, 2, 3),
                new InventoryStack?[] {
                    new(InventoryEntry.FromItem("asteria:stick"))
                }.Concat(new InventoryStack?[26]).ToArray())]);
        var invalid = new GameplaySessionSnapshot(
            saved.Spatial, saved.Name,
            saved.TicksPerSecond, saved.SpawnCreatures, saved.Player, [bad]);
        Assert.Throws<InvalidDataException>(() =>
            Restore(creation, invalid, content));
        Assert.Equal(1, states.Count);
        Assert.Equal(0, sphere.StorageBoxes.Count);
        Assert.True(sphere.World.ContainsChunk(ChunkCoord.Zero));
    }

    private static void Fill(DimensionSessionState sphere,
        WorldVoxelCoord position, string id, int amount, BlockRegistry blocks)
    {
        sphere.World.InsertChunk(ChunkCoord.Zero, new Chunk());
        Assert.True(sphere.World.SetBlockAt(
            position, blocks.GetId("asteria:storage_box"), out _));
        Assert.True(sphere.StorageBoxes.TryOpen(position, sphere.World, blocks));
        Assert.True(sphere.StorageBoxes.TryInsertActive(
            new InventoryStack(InventoryEntry.FromItem(id), amount),
            sphere.World, blocks));
        sphere.StorageBoxes.Close();
    }
}
