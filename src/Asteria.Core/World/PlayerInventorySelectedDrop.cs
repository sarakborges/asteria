using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// One authoritative Q-key release: capacity reservation precedes removal
/// of a single selected unit. A full drop pool must not destroy older drops,
/// consume an inventory item, or advance either owner's revision.
/// </summary>
public static class PlayerInventorySelectedDrop
{
    public static bool TryDrop(
        PlayerInventory inventory, DroppedBlockRuntime drops,
        Vector3 position, Vector3 velocity)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(drops);
        if (inventory.SelectedStack is not { } selected) return false;

        // Publish the portable unit, including orientation, dye, layers and
        // item durability/metadata, before committing inventory consumption.
        if (!drops.TrySpawnWithoutEviction(
                selected.WithQuantity(1), position, velocity))
            return false;
        if (!inventory.TryConsumeSelected())
            throw new InvalidOperationException(
                "Selected stack changed during a committed player drop.");
        return true;
    }
}
