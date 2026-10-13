using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerInventoryClearTests
{
    [Fact]
    public void ClearAllRemovesEveryPossessionAndIncrementsRevisionOnce()
    {
        var item = new InventoryStack(InventoryEntry.FromItem("asteria:stick"), 2);
        var backpack = new InventoryStack?[PlayerInventory.BackpackSlots];
        var hotbar = new InventoryStack?[PlayerInventory.HotbarSlots];
        var equipment = new InventoryStack?[PlayerInventory.EquipmentSlots];
        backpack[0] = item;
        hotbar[0] = item;
        equipment[0] = item.WithQuantity(1);
        var inventory = new PlayerInventory();
        inventory.Restore(new PlayerInventorySnapshot(
            0, backpack, hotbar, item, equipment));
        var revision = inventory.Revision;

        Assert.True(inventory.ClearAll());
        var cleared = inventory.Capture();
        Assert.All(cleared.Backpack, Assert.Null);
        Assert.All(cleared.Hotbar, Assert.Null);
        Assert.All(cleared.Equipment!, Assert.Null);
        Assert.Null(cleared.Cursor);
        Assert.Equal(revision + 1, inventory.Revision);

        Assert.False(inventory.ClearAll());
        Assert.Equal(revision + 1, inventory.Revision);
    }
}
