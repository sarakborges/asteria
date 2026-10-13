using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerInventoryCursorDropTests
{
    private static DroppedBlockRuntime Drops(int maximum = 8) =>
        new(new VoxelWorld(),
            new BlockRegistry([new BlockDefinition("asteria:stone")]),
            maximumActive: maximum);

    [Fact]
    public void CursorDropPublishesExactStackAndVelocityAndConsumesOnlyCursor()
    {
        var inventory = new PlayerInventory();
        var held = InventoryEntry.FromItem("asteria:pebble",
            new Dictionary<string, string> { ["source"] = "coast" });
        Assert.True(inventory.TryCreativePick(held, 37));
        Assert.True(inventory.TryInsert(new InventoryStack(
            InventoryEntry.FromItem("asteria:stick"), 2)));
        var before = inventory.Revision;
        var drops = Drops();
        var position = new Vector3(3, 5, 7);
        var velocity = new Vector3(1.5f, 2, -2.5f);

        Assert.True(PlayerInventoryCursorDrop.TryDrop(
            inventory, drops, position, velocity));

        Assert.Null(inventory.Cursor);
        Assert.Equal(before + 1, inventory.Revision);
        var drop = Assert.Single(drops.ActiveBlocks);
        Assert.Equal("asteria:pebble", drop.Stack.Id);
        Assert.Equal(37, drop.Stack.Quantity);
        Assert.Equal("coast", drop.Stack.Entry.Metadata["source"]);
        Assert.Equal(velocity, drop.Velocity);
        Assert.Equal(position + new Vector3(0.2f, 0.25f, 0f), drop.Position);
        Assert.Equal(2, inventory.SelectedStack!.Quantity);
        Assert.False(PlayerInventoryCursorDrop.TryDrop(
            inventory, drops, position, velocity));
        Assert.Single(drops.ActiveBlocks);
        Assert.Equal(before + 1, inventory.Revision);
    }

    [Fact]
    public void FullDropPoolRejectsTransferWithoutEvictingOrChangingCursor()
    {
        var inventory = new PlayerInventory();
        var item = InventoryEntry.FromTool("asteria:pickaxe_rustic",
            new Dictionary<string, string> { [EquipmentDurability.MetadataKey] = "5" });
        Assert.True(inventory.TryCreativePick(item));
        var cursor = inventory.Cursor;
        var before = inventory.Revision;
        var drops = Drops(maximum: 1);
        var existing = drops.Spawn(new InventoryStack(
            InventoryEntry.FromItem("asteria:stick")), new Vector3(1, 2, 3));

        Assert.False(PlayerInventoryCursorDrop.TryDrop(
            inventory, drops, new Vector3(4, 6, 8), Vector3.UnitZ * 2f));
        Assert.Equal(cursor, inventory.Cursor);
        Assert.Equal(before, inventory.Revision);
        Assert.Equal(existing, Assert.Single(drops.ActiveBlocks).Id);
        Assert.Equal("5", inventory.Cursor!.Entry.Metadata[EquipmentDurability.MetadataKey]);
    }

    [Fact]
    public void InvalidDropPositionAndVelocityNeverConsumeCursor()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryCreativePick(
            InventoryEntry.FromItem("asteria:stick")));
        var before = inventory.Revision;
        var drops = Drops();
        Assert.False(PlayerInventoryCursorDrop.TryDrop(
            inventory, drops, new Vector3(0, -1, 0), Vector3.Zero));
        Assert.False(PlayerInventoryCursorDrop.TryDrop(
            inventory, drops, Vector3.Zero,
            new Vector3(float.NaN, 0f, 0f)));
        Assert.Equal(before, inventory.Revision);
        Assert.NotNull(inventory.Cursor);
        Assert.Empty(drops.ActiveBlocks);
    }
}
