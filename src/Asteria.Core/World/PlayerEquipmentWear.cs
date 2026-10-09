using Asteria.Core.Content;

namespace Asteria.Core.World;

public static class PlayerEquipmentWear
{
    public static bool ApplyContactHit(
        PlayerInventory inventory, PackContentRegistry<ItemDefinition> items)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(items);
        var targets = new List<(EquipmentSlot Slot, string Id, int Max)>();
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var entry = inventory.EquipmentAt(slot)?.Entry;
            if (entry is not { Kind: InventoryEntryKind.Item } ||
                !items.TryGet(entry.Id, out var definition) ||
                definition is null || definition.EquipmentSlot != slot ||
                definition.DamageReduction <= 0f ||
                definition.MaxDurability is not { } max)
                continue;
            EquipmentDurability.Remaining(entry, max);
            targets.Add((slot, entry.Id, max));
        }
        var changed = false;
        foreach (var (slot, id, max) in targets)
            changed |= inventory.WearEquipment(slot, id, max);
        return changed;
    }
}
