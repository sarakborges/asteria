using System.Text.Json;

namespace Asteria.Core.Content;

public sealed record ItemIconVariant(
    string MetadataKey,
    string MetadataValue,
    string Icon);

public sealed record ItemDefinition(
    string Id,
    string Category,
    string Icon,
    IReadOnlyList<ItemIconVariant> IconVariants)
{
    public static ItemDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        var variants = new List<ItemIconVariant>();
        if (root.TryGetProperty("iconVariants", out var values))
        {
            if (values.ValueKind != JsonValueKind.Array)
            {
                throw new FormatException("iconVariants must be an array");
            }

            var keys = new HashSet<(string Key, string Value)>();
            foreach (var item in values.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    throw new FormatException("iconVariants entries must be objects");
                }

                var key = PackContentFields.RequiredString(item, "metadataKey");
                var value = PackContentFields.RequiredString(item, "metadataValue");
                if (!keys.Add((key, value)))
                {
                    throw new FormatException("Duplicate icon variant metadata selector");
                }

                variants.Add(new ItemIconVariant(
                    key, value,
                    PackContentFields.ResourcePath(
                        PackContentFields.RequiredString(item, "icon"),
                        "iconVariants.icon")));
            }
        }

        return new ItemDefinition(
            PackContentFields.Id(root),
            PackContentFields.RequiredString(root, "category"),
            PackContentFields.ResourcePath(PackContentFields.RequiredString(root, "icon"), "icon"),
            variants.AsReadOnly());
    }
}
