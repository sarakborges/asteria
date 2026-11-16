using System.Text.Json;

namespace Asteria.Core.World;

/// <summary>
/// Shared pack-authored independent loot rolls for Blocks and Objects. A present (even empty) table replaces
/// the legacy dropsSelf behavior; an absent table keeps it unchanged.
/// </summary>
public sealed class LootTable
{
    public const int MaximumEntries = 16;
    public const int MaximumQuantityPerEntry = 256;
    private readonly LootEntry[] _entries;

    public LootTable(IEnumerable<LootEntry> entries)
    {
        _entries = entries?.ToArray() ?? throw new ArgumentNullException(nameof(entries));
        if (_entries.Length > MaximumEntries || _entries.Any(entry => entry is null))
            throw new ArgumentException("Block loot table has too many or invalid entries.",
                nameof(entries));
    }

    public IReadOnlyList<LootEntry> Entries => Array.AsReadOnly(_entries);

    public static LootTable Parse(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.Array)
            throw new FormatException("lootTable must be an array.");
        var entries = new List<LootEntry>();
        foreach (var value in json.EnumerateArray())
        {
            if (entries.Count == MaximumEntries)
                throw new FormatException("lootTable exceeds maximum entries.");
            if (value.ValueKind != JsonValueKind.Object ||
                !value.TryGetProperty("item", out var item) ||
                item.ValueKind != JsonValueKind.String)
                throw new FormatException("lootTable entries require an item ID.");
            var id = item.GetString()!;
            var chance = value.TryGetProperty("chance", out var probability)
                ? probability.ValueKind == JsonValueKind.Number
                    ? probability.GetSingle()
                    : throw new FormatException("lootTable chance must be a number.")
                : 1f;
            var quantity = value.TryGetProperty("quantity", out var count)
                ? count.ValueKind == JsonValueKind.Number &&
                  count.TryGetInt32(out var result)
                    ? result
                    : throw new FormatException("lootTable quantity must be an integer.")
                : 1;
            entries.Add(new LootEntry(id, chance, quantity));
        }
        return new LootTable(entries);
    }
}

public sealed record LootEntry
{
    public LootEntry(string item, float chance = 1f, int quantity = 1)
    {
        BlockDefinition.ValidateId(item);
        if (!float.IsFinite(chance) || chance is < 0f or > 1f)
            throw new ArgumentOutOfRangeException(nameof(chance));
        if (quantity is < 1 or > LootTable.MaximumQuantityPerEntry)
            throw new ArgumentOutOfRangeException(nameof(quantity));
        Item = item;
        Chance = chance;
        Quantity = quantity;
    }

    public string Item { get; }
    public float Chance { get; }
    public int Quantity { get; }
}
