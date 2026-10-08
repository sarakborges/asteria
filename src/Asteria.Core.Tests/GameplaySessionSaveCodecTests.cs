using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class GameplaySessionSaveCodecTests
{
    private static DimensionDefinition Dimension(string id) => new(
        new DimensionId(id), [id + "/plain"], 90, 18f,
        new DimensionSpawnDefinition(0, 0),
        new DimensionEnvironmentDefinition(
            new DimensionColor(0, 0, 0), new DimensionColor(255, 255, 255),
            1f, new DimensionColor(0, 0, 0), 0f));

    private static DimensionRegistry Dimensions() => new([
        Dimension("asteria:overworld"),
        Dimension("asteria:umbral"),
        Dimension("asteria:unused")
    ]);

    private static (BlockRegistry Blocks, FluidRegistry Fluids,
        DyeRegistry Dyes, AttachedLayerRegistry Layers) Content() => (
        new BlockRegistry([new BlockDefinition("asteria:stone")]),
        new FluidRegistry([]), new DyeRegistry([]),
        new AttachedLayerRegistry([])
    );

    private static GameplaySessionSnapshot Capture(
        DimensionSessionStateStore store,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        GameplaySessionSaveCodec.Capture(
            store, content.Blocks, content.Fluids, content.Dyes, content.Layers);

    private static DimensionSessionStateStore Restore(
        WorldCreationOptions creation, DimensionRegistry dimensions,
        GameplaySessionSnapshot saved,
        (BlockRegistry Blocks, FluidRegistry Fluids,
            DyeRegistry Dyes, AttachedLayerRegistry Layers) content) =>
        GameplaySessionSaveCodec.Restore(creation, dimensions, saved,
            content.Blocks, content.Fluids, content.Dyes, content.Layers);

    [Fact]
    public void RestoresCursorSlotsPlayerFlightRulesAndIsolatedSphereClocks()
    {
        var content = Content();
        var dimensions = Dimensions();
        var creation = new WorldCreationOptions("Example", 1122UL);
        var source = new DimensionSessionStateStore(creation, dimensions);
        var surface = source.GetOrCreate(DimensionId.Overworld);
        var umbral = source.GetOrCreate(new DimensionId("asteria:umbral"));

        source.Player.SetGameMode(PlayerGameMode.Creative);
        Assert.True(source.Player.JumpPressed(100));
        Assert.True(source.Player.JumpPressed(101));
        Assert.True(source.Player.IsFlying);

        var tool = InventoryEntry.FromTool("asteria:rustic_pickaxe",
            new Dictionary<string, string> { ["wear"] = "12" });
        Assert.True(source.Player.Inventory.TryInsert(new InventoryStack(tool)));
        Assert.True(source.Player.Inventory.SelectHotbar(3));
        Assert.True(source.Player.Inventory.TryCreativePick(
            InventoryEntry.FromItem("asteria:pebble"), 31));
        Assert.True(source.Player.Inventory.ClickSlot(0));
        Assert.True(source.Player.Inventory.TryCreativePick(
            InventoryEntry.FromItem("asteria:stick"), 7));

        source.GameRules.SetTicksPerSecond(60);
        source.GameRules.SetSpawnCreatures(false);
        surface.WorldTick = 400;
        surface.DayNight = new DayNightClockState(8, 43);
        surface.PlayerPosition = new Vector3(4.5f, 71f, -12.25f);
        surface.NaturalSpawnNextAttemptTick = 420;
        umbral.WorldTick = 5;
        umbral.DayNight = new DayNightClockState(2, 15);
        umbral.PlayerPosition = new Vector3(-9f, 34f, 1f);

        var saved = Capture(source, content);
        var restored = Restore(creation, dimensions, saved, content);
        Assert.NotSame(source, restored);
        Assert.Equal(60U, restored.GameRules.TicksPerSecond);
        Assert.False(restored.GameRules.SpawnCreatures);
        Assert.Equal(PlayerGameMode.Creative, restored.Player.GameMode);
        Assert.True(restored.Player.IsFlying);

        var inventory = restored.Player.Inventory;
        Assert.Equal(3, inventory.SelectedSlot);
        Assert.Equal(tool, inventory.SlotAt(PlayerInventory.BackpackSlots)!.Entry);
        Assert.Equal(31, inventory.SlotAt(0)!.Quantity);
        Assert.Equal("asteria:stick", inventory.Cursor!.Id);
        Assert.Equal(7, inventory.Cursor.Quantity);

        var restoredSurface = restored.GetOrCreate(DimensionId.Overworld);
        var restoredUmbral = restored.GetOrCreate(new DimensionId("asteria:umbral"));
        Assert.Equal(400UL, restoredSurface.WorldTick);
        Assert.Equal(new DayNightClockState(8, 43), restoredSurface.DayNight);
        Assert.Equal(new Vector3(4.5f, 71f, -12.25f), restoredSurface.PlayerPosition);
        Assert.Equal(420UL, restoredSurface.NaturalSpawnNextAttemptTick);
        Assert.Equal(5UL, restoredUmbral.WorldTick);
        Assert.Equal(new DayNightClockState(2, 15), restoredUmbral.DayNight);
        Assert.Equal(new Vector3(-9f, 34f, 1f), restoredUmbral.PlayerPosition);
        Assert.False(restored.TryGet(new DimensionId("asteria:unused"), out _));
    }

    [Fact]
    public void CapturedInventoryCannotBeChangedThroughExposedArrays()
    {
        var content = Content();
        var creation = new WorldCreationOptions("World", 1UL);
        var source = new DimensionSessionStateStore(creation, Dimensions());
        var pebble = new InventoryStack(InventoryEntry.FromItem("asteria:pebble"), 12);
        Assert.True(source.Player.Inventory.TryInsert(pebble));
        var saved = Capture(source, content);

        var view = saved.Player.Inventory;
        view.Hotbar[0] = null;
        view.Backpack[0] = pebble;
        var second = saved.Player.Inventory;
        Assert.Equal(pebble, second.Hotbar[0]);
        Assert.Null(second.Backpack[0]);

        var restored = Restore(creation, Dimensions(), saved, content);
        Assert.Equal(pebble, restored.Player.Inventory.SelectedStack);
    }

    [Fact]
    public void RejectsCorruptInventoryFlightPositionAndSphereIdentity()
    {
        var invalidSlots = new PlayerInventorySnapshot(
            0, new InventoryStack?[0],
            new InventoryStack?[PlayerInventory.HotbarSlots], null);
        Assert.Throws<InvalidDataException>(() => new PlayerSessionSnapshot(
            PlayerGameMode.Survival, false, invalidSlots));
        var inventory = new PlayerInventory().Capture();
        Assert.Throws<InvalidDataException>(() => new PlayerSessionSnapshot(
            PlayerGameMode.Survival, true, inventory));
        Assert.Throws<InvalidDataException>(() => new PlayerSessionSnapshot(
            PlayerGameMode.Spectator, false, inventory));
        Assert.Throws<InvalidDataException>(() => new SphereClockSnapshot(
            DimensionId.Overworld, 0, new DayNightClockState(0, 10), null, 0));
        Assert.Throws<InvalidDataException>(() => new SphereClockSnapshot(
            DimensionId.Overworld, 0, null, new Vector3(1, -2, 3), 0));

        var content = Content();
        var creation = new WorldCreationOptions("World", 42UL);
        var source = new DimensionSessionStateStore(creation, Dimensions());
        source.GetOrCreate(DimensionId.Overworld);
        var saved = Capture(source, content);

        Assert.Throws<InvalidDataException>(() => new GameplaySessionSnapshot(
            saved.Spatial, saved.Name, 40, true, saved.Player,
            [new SphereClockSnapshot(
                new DimensionId("asteria:umbral"), 0, null, null, 0)]));
        Assert.Throws<InvalidDataException>(() => Restore(
            new WorldCreationOptions("Other Name", 42UL),
            Dimensions(), saved, content));
    }
}
