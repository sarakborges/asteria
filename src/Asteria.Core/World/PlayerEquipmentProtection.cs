using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Read-only defense calculation from actual occupied equipment slots.
/// Invalid/restored mismatched items never contribute bonuses.
/// </summary>
public static class PlayerEquipmentProtection
{
    public const float MaximumReduction = 0.75f;

    public static float Calculate(
        PlayerInventory inventory, PackContentRegistry<ItemDefinition> items)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(items);
        var reduction = 0f;
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var entry = inventory.EquipmentAt(slot)?.Entry;
            if (entry is not { Kind: InventoryEntryKind.Item } ||
                !items.TryGet(entry.Id, out var definition) ||
                definition is null || definition.EquipmentSlot != slot)
                continue;
            if (definition.MaxDurability is { } capacity &&
                EquipmentDurability.Remaining(entry, capacity) > 0)
                reduction += definition.DamageReduction;
        }
        return MathF.Min(MaximumReduction, reduction);
    }
}
