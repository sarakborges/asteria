using System.Numerics;
using Asteria.Core.Content;

namespace Asteria.Core.World;

/// <summary>
/// Shared deterministic, bounded reward preparation. Only canonical mutation
/// owners publish the resulting physical stacks after successful preflight.
/// </summary>
public static class LootTableRoller
{
    public static List<InventoryStack> Roll(
        LootTable table, InventoryContentCatalog catalog,
        WorldVoxelCoord position, ulong worldTick,
        InventoryEntry? selfEntry = null, uint salt = 0)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(catalog);
        var random = LootSeed(position, worldTick) ^ salt;
        if (random == 0) random = 0xA341316Cu;
        var stacks = new List<InventoryStack>();
        foreach (var loot in table.Entries)
        {
            if (loot.Chance < 1f && NextUnit(ref random) >= loot.Chance)
                continue;
            if (!catalog.TryResolveLoot(loot.Item, out var resolved) ||
                resolved is null)
                throw new InvalidOperationException(
                    $"Unknown loot entry {loot.Item}.");

            // The portable block self-drop keeps orientation, sculpting,
            // tint, dye and face layers from the actual mined voxel.
            var entry = selfEntry is not null &&
                resolved.Kind == selfEntry.Kind &&
                resolved.Id == selfEntry.Id ? selfEntry : resolved;
            var remaining = loot.Quantity;
            while (remaining > 0)
            {
                var quantity = Math.Min(remaining, entry.MaxStackSize);
                stacks.Add(new InventoryStack(entry, quantity));
                remaining -= quantity;
            }
        }
        return stacks;
    }

    /// <summary>
    /// Absent Object table preserves dropSelf/pickupItem. Explicit empty tables
    /// produce no physical rewards. Pickup-only interactions remain unchanged.
    /// </summary>
    public static IReadOnlyList<InventoryStack> ResolveObject(
        ObjectDefinition definition, ObjectCell obj,
        WorldVoxelCoord support, InventoryContentCatalog? catalog,
        ulong worldTick)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.LootTable is null)
            return definition.DropSelf
                ? [new InventoryStack(definition.RewardEntry)] : [];
        if (definition.LootTable.Entries.Count == 0)
            return [];
        if (catalog is null)
            throw new InvalidOperationException(
                $"Object {definition.Id} has a lootTable but no content catalog is configured.");
        return Roll(definition.LootTable, catalog, support, worldTick,
            salt: ObjectSalt(obj));
    }

    // Portable deterministic salt: never process-randomized string hash.
    private static uint ObjectSalt(ObjectCell obj)
    {
        var hash = 2166136261u;
        foreach (var c in obj.Id)
            hash = Mix(hash, c);
        hash = Mix(hash, (uint)obj.Face);
        hash = Mix(hash, (uint)obj.Rotation);
        hash = Mix(hash, unchecked((uint)obj.Transform.OffsetX));
        hash = Mix(hash, unchecked((uint)obj.Transform.OffsetY));
        hash = Mix(hash, unchecked((uint)obj.Transform.OffsetZ));
        hash = Mix(hash, obj.Transform.ScaleX);
        hash = Mix(hash, obj.Transform.ScaleY);
        return Mix(hash, obj.Transform.ScaleZ);
    }

    private static uint Mix(uint hash, uint value) =>
        unchecked((hash ^ value) * 16777619u);

    private static uint LootSeed(WorldVoxelCoord position, ulong tick)
    {
        var seed = unchecked((uint)tick) ^
            BitOperations.RotateLeft(unchecked((uint)(tick >> 32)), 11);
        seed ^= unchecked((uint)position.X * 0x9E3779B9u);
        seed ^= unchecked((uint)position.Y * 0x85EBCA6Bu);
        seed ^= unchecked((uint)position.Z * 0xC2B2AE35u);
        return seed == 0 ? 0xA341316Cu : seed;
    }

    private static float NextUnit(ref uint random)
    {
        random ^= random << 13;
        random ^= random >> 17;
        random ^= random << 5;
        return (random >> 8) * (1f / 16777216f);
    }
}
