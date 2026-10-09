using System.Text.Json;

namespace Asteria.Core.Content;

/// <summary>
/// Authored creative grouping, shared across block/item/tool/layer kinds.
/// IDs intentionally match MineClone's unnamespaced category identities.
/// </summary>
public sealed record InventoryCategoryDefinition(
    string Id, ushort Order, string Icon)
{
    public static InventoryCategoryDefinition Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var id = PackContentFields.RequiredString(root, "id");
        if (id.Length > 64 || id.Any(c =>
            !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')))
            throw new FormatException("Invalid creative inventory category ID.");

        if (!root.TryGetProperty("order", out var orderValue) ||
            orderValue.ValueKind != JsonValueKind.Number ||
            !orderValue.TryGetUInt16(out var order))
            throw new FormatException("Category order must be an unsigned 16-bit value.");

        var icon = PackContentFields.ResourcePath(
            PackContentFields.RequiredString(root, "icon"), "icon");
        if (!icon.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new FormatException("Category icon must be a PNG resource.");

        return new InventoryCategoryDefinition(id, order, icon);
    }
}

public sealed class InventoryCategoryRegistry
{
    private readonly IReadOnlyDictionary<string, InventoryCategoryDefinition> _byId;
    private readonly InventoryCategoryDefinition[] _ordered;

    public InventoryCategoryRegistry(IEnumerable<InventoryCategoryDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _ordered = definitions
            .OrderBy(definition => definition.Order)
            .ThenBy(definition => definition.Id, StringComparer.Ordinal)
            .ToArray();
        if (_ordered.Length == 0 || _ordered.Length > 128)
            throw new ArgumentException("Invalid creative inventory category count.");

        var byId = new Dictionary<string, InventoryCategoryDefinition>(
            _ordered.Length, StringComparer.Ordinal);
        foreach (var definition in _ordered)
            if (definition is null || !byId.TryAdd(definition.Id, definition))
                throw new ArgumentException("Repeated or missing creative category ID.");
        _byId = byId;
    }

    public IReadOnlyList<InventoryCategoryDefinition> Definitions => _ordered;

    public bool TryGet(string id, out InventoryCategoryDefinition? definition) =>
        _byId.TryGetValue(id, out definition);

    public InventoryCategoryDefinition Get(string id) =>
        _byId.TryGetValue(id, out var definition)
            ? definition
            : throw new InvalidDataException($"Unknown creative inventory category: {id}");

    public static InventoryCategoryRegistry FromJson(IEnumerable<string> documents) =>
        new(documents.Select(InventoryCategoryDefinition.Parse));
}
