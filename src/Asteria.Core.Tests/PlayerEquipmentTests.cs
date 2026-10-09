using Asteria.Core.World;
using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class PlayerEquipmentTests
{
    private static InventoryEntry Helmet() =>
        InventoryEntry.FromItem("asteria:helm_test", maxStackSize: 1);
    private static readonly Func<InventoryEntry, EquipmentSlot, bool> Rules =
        (entry, slot) => entry.Equals(Helmet()) && slot == EquipmentSlot.Helmet;

    [Fact]
    public void EquippingSwapsWithCursorAndIsLossless()
    {
        var player = new PlayerInventory();
        Assert.True(player.TryCreativePick(Helmet()));
        Assert.True(player.ClickEquipment(EquipmentSlot.Helmet, Rules));
        Assert.Null(player.Cursor);
        Assert.Equal(Helmet(), player.EquipmentAt(EquipmentSlot.Helmet)!.Entry);
        Assert.True(player.ClickEquipment(EquipmentSlot.Helmet, Rules));
        Assert.Null(player.EquipmentAt(EquipmentSlot.Helmet));
        Assert.Equal(Helmet(), player.Cursor!.Entry);
    }

    [Fact]
    public void InvalidEquipDoesNotConsumeCursorOrChangeRevision()
    {
        var inventory = new PlayerInventory();
        var invalid = InventoryEntry.FromItem("asteria:stone", maxStackSize: 64);
        Assert.True(inventory.TryCreativePick(invalid, 2));
        var version = inventory.Revision;
        Assert.False(inventory.ClickEquipment(EquipmentSlot.Helmet, Rules));
        Assert.False(inventory.ClickEquipment((EquipmentSlot)255, Rules));
        Assert.Equal(version, inventory.Revision);
        Assert.Equal(2, inventory.Cursor!.Quantity);
        Assert.Null(inventory.EquipmentAt(EquipmentSlot.Helmet));
    }

    [Fact]
    public void SnapshotDefendsAgainstInvalidShapesAndRestoresAllSlots()
    {
        var inventory = new PlayerInventory();
        Assert.True(inventory.TryCreativePick(Helmet()));
        Assert.True(inventory.ClickEquipment(EquipmentSlot.Helmet, Rules));
        var saved = inventory.Capture();
        var other = new PlayerInventory();
        other.Restore(saved);
        Assert.Equal(Helmet(), other.EquipmentAt(EquipmentSlot.Helmet)!.Entry);

        var bad = saved with
        {
            Equipment = [new InventoryStack(
                InventoryEntry.FromItem("asteria:stack", maxStackSize: 64), 2), null, null, null]
        };
        Assert.Throws<InvalidDataException>(() => other.Restore(bad));
        Assert.Equal(Helmet(), other.EquipmentAt(EquipmentSlot.Helmet)!.Entry);
    }

    [Fact]
    public void LegacySnapshotHasEmptyEquipment()
    {
        var inventory = new PlayerInventory();
        inventory.Restore(new PlayerInventorySnapshot(
            0, new InventoryStack?[PlayerInventory.BackpackSlots],
            new InventoryStack?[PlayerInventory.HotbarSlots], null));
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
            Assert.Null(inventory.EquipmentAt(slot));
    }

    [Fact]
    public void ItemDefinitionRejectsInvalidEquipSlotAndNonUnitaryGear()
    {
        var valid = ItemDefinition.Parse("""
            {"id":"asteria:cap","category":"tools","icon":"textures/items/cap.png",
             "maxStackSize":1,"equipmentSlot":"helmet"}
            """);
        Assert.Equal(EquipmentSlot.Helmet, valid.EquipmentSlot);
        Assert.Throws<FormatException>(() => ItemDefinition.Parse("""
            {"id":"asteria:cap","category":"tools","icon":"textures/items/cap.png",
             "maxStackSize":2,"equipmentSlot":"helmet"}
            """));
        Assert.Throws<FormatException>(() => ItemDefinition.Parse("""
            {"id":"asteria:cap","category":"tools","icon":"textures/items/cap.png",
             "maxStackSize":1,"equipmentSlot":"helmet../"}
            """));
    }
}
