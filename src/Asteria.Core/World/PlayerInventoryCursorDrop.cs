using System.Numerics;

namespace Asteria.Core.World;

/// <summary>
/// Atomic transfer from the player's UI cursor to the existing authoritative
/// world drop runtime. Failed capacity preflight cannot consume the cursor
/// or evict an older physical item.
/// </summary>
public static class PlayerInventoryCursorDrop
{
    public static bool TryDrop(
        PlayerInventory inventory,
        DroppedBlockRuntime drops,
        Vector3 position,
        Vector3 velocity)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(drops);
        if (inventory.Cursor is not { } held) return false;

        // One entity retains the complete stack, block state and metadata.
        if (!drops.TrySpawnBatch([held], position, velocity)) return false;

        if (!inventory.DiscardCursor())
            throw new InvalidOperationException(
                "Cursor changed during a committed item drop.");
        return true;
    }
}
