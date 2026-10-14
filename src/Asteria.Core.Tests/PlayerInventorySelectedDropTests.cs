using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerInventorySelectedDropTests
{
    private static readonly Vector3 Origin = new(3f, 5f, 7f);
    private static readonly Vector3 Velocity = new(2f, 1.5f, -3f);

    private static DroppedBlockRuntime Drops(
        int maximum = 8, DroppedBlockRuntimeSnapshot? restore = null) =>
        new(new VoxelWorld(),
            new BlockRegistry([new BlockDefinition("asteria:stone")]),
            maximumActive: maximum, restore: restore);

    [Fact]
    public void QTransfersOnePortableItemWithoutLosingStackMetadataOrVelocity()
    {
        var inventory = new PlayerInventory();
        var selected = InventoryEntry.FromTool("asteria:dimensional_slicer",
            new Dictionary<string, string> { ["target_dimension"] = "asteria:umbral" },
            maxStackSize: 1);
        Assert.True(inventory.TryInsert(new InventoryStack(selected)));
        var drops = Drops();
        var before = inventory.Revision;

        Assert.True(PlayerInventorySelectedDrop.TryDrop(
            inventory, drops, Origin, Velocity));
        Assert.Null(inventory.SelectedStack);
        Assert.Equal(before + 1, inventory.Revision);
        var drop = Assert.Single(drops.ActiveBlocks);
        Assert.Equal(selected, drop.Stack.Entry);
        Assert.Equal(1, drop.Stack.Quantity);
        Assert.Equal(Origin, drop.Position);
        Assert.Equal(Velocity, drop.Velocity);
        Assert.Equal("asteria:umbral",
            drop.Stack.Entry.Metadata["target_dimension"]);

        var restored = Drops(restore: drops.CaptureState());
        var afterLoad = Assert.Single(restored.ActiveBlocks);
        Assert.Equal(drop, afterLoad);
        Assert.Equal(selected, afterLoad.Stack.Entry);
    }

    [Fact]
    public void QConsumesOnlyOneUnitAndDoesNotTouchOtherInventorySlots()
    {
        var inventory = new PlayerInventory();
        var entry = InventoryEntry.FromItem("asteria:pebble",
            new Dictionary<string, string> { ["grade"] = "fine" });
        Assert.True(inventory.TryInsert(new InventoryStack(entry, 12)));
        Assert.True(inventory.TryInsert(
            new InventoryStack(InventoryEntry.FromItem("asteria:stick"), 4)));
        var other = inventory.SlotAt(PlayerInventory.BackpackSlots + 1);
        var drops = Drops();

        Assert.True(PlayerInventorySelectedDrop.TryDrop(
            inventory, drops, Origin, Velocity));
        Assert.Equal(11, inventory.SelectedStack!.Quantity);
        Assert.Equal(entry, inventory.SelectedStack.Entry);
        Assert.Equal(other, inventory.SlotAt(PlayerInventory.BackpackSlots + 1));
        Assert.Equal(1, Assert.Single(drops.ActiveBlocks).Stack.Quantity);
        Assert.Equal(entry, Assert.Single(drops.ActiveBlocks).Stack.Entry);
    }

    [Fact]
    public void FullDropPoolNeverEvictsExistingItemOrConsumesSelectedStack()
    {
        var inventory = new PlayerInventory();
        var selected = InventoryEntry.FromItem("asteria:pebble",
            new Dictionary<string, string> { ["grade"] = "fine" });
        Assert.True(inventory.TryInsert(new InventoryStack(selected, 15)));
        var drops = Drops(maximum: 1);
        var old = drops.Spawn(
            new InventoryStack(InventoryEntry.FromItem("asteria:old")),
            new Vector3(2, 5, 7));
        var snapshot = drops.CaptureState();
        var before = inventory.Capture();
        var revision = inventory.Revision;

        Assert.False(PlayerInventorySelectedDrop.TryDrop(
            inventory, drops, Origin, Velocity));
        Assert.Equal(revision, inventory.Revision);
        var after = inventory.Capture();
        Assert.Equal(before.SelectedSlot, after.SelectedSlot);
        Assert.Equal(before.Backpack, after.Backpack);
        Assert.Equal(before.Hotbar, after.Hotbar);
        Assert.Equal(before.Cursor, after.Cursor);
        Assert.Equal(before.Equipment, after.Equipment);
        var dropAfter = drops.CaptureState();
        Assert.Equal(snapshot.NextId, dropAfter.NextId);
        Assert.Equal(snapshot.ActiveBlocks, dropAfter.ActiveBlocks);
        Assert.Equal(old, Assert.Single(drops.ActiveBlocks).Id);

        // After an ordinary collection frees capacity, the same Q request
        // succeeds with the original selected item and no old-item eviction.
        drops.Advance(0.5, 0);
        Assert.Equal(1, drops.CollectNearby(
            new Vector3(2, 5, 7), 1f, _ => true));
        Assert.True(PlayerInventorySelectedDrop.TryDrop(
            inventory, drops, Origin, Velocity));
        Assert.Equal(14, inventory.SelectedStack!.Quantity);
        Assert.Equal(selected, Assert.Single(drops.ActiveBlocks).Stack.Entry);
    }

    [Fact]
    public void InvalidCoordinatesOrEmptyHotbarNeverConsumeInventory()
    {
        var inventory = new PlayerInventory();
        var drops = Drops();
        Assert.False(PlayerInventorySelectedDrop.TryDrop(
            inventory, drops, Origin, Velocity));
        Assert.Equal(0UL, inventory.Revision);

        Assert.True(inventory.TryInsert(new InventoryStack(
            InventoryEntry.FromItem("asteria:stick"), 4)));
        var revision = inventory.Revision;
        foreach (var position in new[] {
            new Vector3(3, -1, 7),
            new Vector3(float.NaN, 5, 7),
            new Vector3(float.PositiveInfinity, 5, 7) })
        {
            Assert.False(PlayerInventorySelectedDrop.TryDrop(
                inventory, drops, position, Velocity));
        }
        Assert.False(PlayerInventorySelectedDrop.TryDrop(
            inventory, drops, Origin,
            new Vector3(0, float.NaN, 0)));
        Assert.Equal(revision, inventory.Revision);
        Assert.Equal(4, inventory.SelectedStack!.Quantity);
        Assert.Empty(drops.ActiveBlocks);
    }

    [Fact]
    public void DroppedBlockKeepsExactPortableOrientation()
    {
        var blocks = new BlockRegistry([new BlockDefinition("asteria:stone")]);
        var stone = BlockStateSnapshot.FromCell(new VoxelCell(
            blocks.GetId("asteria:stone"), orientation: BlockOrientation.X));
        var entry = InventoryEntry.FromBlock("asteria:stone", stone);
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryInsert(new InventoryStack(entry, 3)));
        var drops = Drops();
        Assert.True(PlayerInventorySelectedDrop.TryDrop(
            inventory, drops, Origin, Velocity));
        Assert.Equal(stone, Assert.Single(drops.ActiveBlocks).Stack.Block);
        Assert.Equal(2, inventory.SelectedStack!.Quantity);
    }

    [Fact]
    public void ExhaustedPersistedDropIdsNeverEvictOlderDrops()
    {
        var before = Drops(maximum: 1);
        var old = before.Spawn(
            new InventoryStack(InventoryEntry.FromItem("asteria:old")), Origin);
        var snapshot = before.CaptureState() with { NextId = ulong.MaxValue };
        var restored = Drops(maximum: 1, restore: snapshot);
        Assert.False(restored.TrySpawnWithoutEviction(
            new InventoryStack(InventoryEntry.FromItem("asteria:new")),
            Origin));
        Assert.Throws<OverflowException>(() =>
            restored.Spawn(new InventoryStack(
                InventoryEntry.FromItem("asteria:new")), Origin));
        Assert.Equal(old, Assert.Single(restored.ActiveBlocks).Id);
        Assert.Equal(ulong.MaxValue, restored.CaptureState().NextId);
    }
}
