using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerInventoryClearTests
{
    [Fact]
    public void TrashShortcutClearsOnlyBackpackAndHotbar()
    {
        var stack = new InventoryStack(InventoryEntry.FromItem("asteria:stick"), 2);
        var helmet = new InventoryStack(InventoryEntry.FromItem("asteria:helmet"));
        var backpack = new InventoryStack?[PlayerInventory.BackpackSlots];
        var hotbar = new InventoryStack?[PlayerInventory.HotbarSlots];
        var equipment = new InventoryStack?[PlayerInventory.EquipmentSlots];
        backpack[0] = stack;
        hotbar[0] = stack;
        equipment[0] = helmet;

        var inventory = new PlayerInventory();
        inventory.Restore(new PlayerInventorySnapshot(
            0, backpack, hotbar, stack, equipment));
        var revision = inventory.Revision;

        Assert.True(inventory.ClearBackpackAndHotbar());
        var cleared = inventory.Capture();
        Assert.All(cleared.Backpack, Assert.Null);
        Assert.All(cleared.Hotbar, Assert.Null);
        Assert.Equal(helmet, cleared.Equipment![0]);
        Assert.Equal(stack, cleared.Cursor);
        Assert.Equal(revision + 1, inventory.Revision);

        Assert.False(inventory.ClearBackpackAndHotbar());
        Assert.Equal(revision + 1, inventory.Revision);
        Assert.Equal(helmet, inventory.EquipmentAt(EquipmentSlot.Helmet));
    }

    [Fact]
    public void DeathClearStillRemovesEquipmentAndCursor()
    {
        var stack = new InventoryStack(InventoryEntry.FromItem("asteria:stick"));
        var backpack = new InventoryStack?[PlayerInventory.BackpackSlots];
        var hotbar = new InventoryStack?[PlayerInventory.HotbarSlots];
        var equipment = new InventoryStack?[PlayerInventory.EquipmentSlots];
        backpack[0] = stack;
        equipment[0] = stack;

        var inventory = new PlayerInventory();
        inventory.Restore(new PlayerInventorySnapshot(
            0, backpack, hotbar, stack, equipment));
        inventory.ClearForDeath();
        var cleared = inventory.Capture();
        Assert.All(cleared.Backpack, Assert.Null);
        Assert.All(cleared.Hotbar, Assert.Null);
        Assert.All(cleared.Equipment!, Assert.Null);
        Assert.Null(cleared.Cursor);
    }
}
