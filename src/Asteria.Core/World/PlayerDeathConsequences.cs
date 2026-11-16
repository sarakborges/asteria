using System.Numerics;

namespace Asteria.Core.World;

public readonly record struct PlayerDeathOutcome(
    bool InventoryKept, int DroppedStacks, bool DropCapacityExceeded);

/// <summary>
/// Transaction boundary for player death possessions. No save, Godot node
/// or browser state is allowed to clear a player inventory independently.
/// </summary>
public static class PlayerDeathConsequences
{
    public static PlayerDeathOutcome Resolve(
        PlayerSessionState player, WorldGameRules rules,
        DroppedBlockRuntime drops, Vector3 deathPosition)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(drops);
        if (!player.Health.IsDead)
            throw new InvalidOperationException("Death effects require a dead player.");
        if (rules.KeepInventory)
            return new PlayerDeathOutcome(true, 0, false);

        var inventory = player.Inventory;
        var snapshot = inventory.Capture();
        var stacks = snapshot.Backpack.Concat(snapshot.Hotbar)
            .Concat(snapshot.Equipment ?? [])
            .Append(snapshot.Cursor)
            .OfType<InventoryStack>()
            .ToArray();
        if (stacks.Length == 0)
            return new PlayerDeathOutcome(false, 0, false);
        if (!drops.TrySpawnBatch(stacks, deathPosition))
            return new PlayerDeathOutcome(true, 0, true);

        inventory.ClearForDeath();
        return new PlayerDeathOutcome(false, stacks.Length, false);
    }
}
