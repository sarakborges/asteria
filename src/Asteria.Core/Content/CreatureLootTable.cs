using System.Text.Json;

namespace Asteria.Core.Content;

/// <summary>Portable item rewards authored on a creature definition.</summary>
public sealed record CreatureLootEntry(string ItemId, float Chance, int Quantity);

public static class CreatureLootTable
{
    public const int MaximumEntries = 16;
    public const int MaximumQuantityPerEntry = 16;

    public static IReadOnlyList<CreatureLootEntry> Parse(JsonElement definition)
    {
        if (!definition.TryGetProperty("lootTable", out var source))
            return Array.Empty<CreatureLootEntry>();
        if (source.ValueKind != JsonValueKind.Array ||
            source.GetArrayLength() > MaximumEntries)
            throw new FormatException("Creature lootTable must be a bounded array.");

        var entries = new List<CreatureLootEntry>();
        foreach (var item in source.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                throw new FormatException("Creature loot entry must be an object.");

            var id = PackContentFields.Namespaced(
                PackContentFields.RequiredString(item, "item"), "lootTable.item");
            var chance = item.TryGetProperty("chance", out _)
                ? PackContentFields.NonNegativeFloat(item, "chance") : 1f;
            if (chance > 1f)
                throw new FormatException("Creature loot chance must be between zero and one.");

            var quantity = item.TryGetProperty("quantity", out _)
                ? PackContentFields.PositiveInt(item, "quantity") : 1;
            if (quantity > MaximumQuantityPerEntry)
                throw new FormatException("Creature loot quantity exceeds the per-entry cap.");

            entries.Add(new CreatureLootEntry(id, chance, quantity));
        }

        return entries.AsReadOnly();
    }

    /// <summary>
    /// Deterministic rolls depend only on the creature instance and authored
    /// table order. Calling twice produces identical results without consuming
    /// shared random state or depending on process-randomized hash codes.
    /// </summary>
    public static IReadOnlyList<CreatureLootEntry> Roll(
        IReadOnlyList<CreatureLootEntry> entries,
        ulong creatureInstanceId)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var result = new List<CreatureLootEntry>();
        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            if (entry.Chance == 0f) continue;
            if (entry.Chance == 1f ||
                UnitHash(creatureInstanceId, (uint)index) < entry.Chance)
                result.Add(entry);
        }
        return result;
    }

    private static double UnitHash(ulong id, uint index)
    {
        var value = id ^ (id >> 33) ^ ((ulong)(index + 1) * 0x9e3779b97f4a7c15ul);
        value ^= value >> 30;
        value *= 0xbf58476d1ce4e5b9ul;
        value ^= value >> 27;
        value *= 0x94d049bb133111ebul;
        value ^= value >> 31;
        return (value >> 11) * (1.0 / (1ul << 53));
    }
}
