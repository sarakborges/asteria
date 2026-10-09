using System.Text.Json;

namespace Asteria.Core.Content;

public sealed record ToolMiningDefinition(
    string Category,
    float Speed);

public sealed record ToolIconVariant(
    string MetadataKey, string MetadataValue, string Icon);

public sealed record ToolDefinition(
    string Id,
    string Category,
    string Icon,
    string? TintIcon,
    string LeftBehavior,
    string RightBehavior,
    ToolMiningDefinition? Mining,
    int MaxStackSize = 1,
    IReadOnlyList<ToolIconVariant>? IconVariants = null)
{
    public static ToolDefinition Parse(string json)
    {
        ArgumentNullException.ThrowIfNull(json);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        ToolMiningDefinition? mining = null;
        if (root.TryGetProperty("mining", out var miningValue))
        {
            if (miningValue.ValueKind != JsonValueKind.Object)
            {
                throw new FormatException("mining must be an object");
            }

            mining = new ToolMiningDefinition(
                PackContentFields.RequiredString(miningValue, "category"),
                PackContentFields.PositiveFloat(miningValue, "speed"));
        }

        var maxStackSize = root.TryGetProperty("maxStackSize", out _)
            ? PackContentFields.PositiveInt(root, "maxStackSize")
            : 1;
        if (maxStackSize > 64)
            throw new FormatException("maxStackSize must not exceed 64");

        var iconVariants = new List<ToolIconVariant>();
        if (root.TryGetProperty("iconVariants", out var variants))
        {
            if (variants.ValueKind != JsonValueKind.Array || variants.GetArrayLength() > 64)
                throw new FormatException("Tool iconVariants must be a bounded array.");
            var keys = new HashSet<(string, string)>();
            foreach (var variant in variants.EnumerateArray())
            {
                if (variant.ValueKind != JsonValueKind.Object)
                    throw new FormatException("Invalid tool icon variant.");
                var key = PackContentFields.RequiredString(variant, "metadataKey");
                var value = PackContentFields.RequiredString(variant, "metadataValue");
                var icon = PackContentFields.ResourcePath(
                    PackContentFields.RequiredString(variant, "icon"),
                    "iconVariants.icon");
                if (!keys.Add((key, value)))
                    throw new FormatException("Duplicate tool icon variant.");
                iconVariants.Add(new ToolIconVariant(key, value, icon));
            }
        }

        var tint = PackContentFields.OptionalString(root, "tintIcon");
        return new ToolDefinition(
            PackContentFields.Id(root),
            PackContentFields.RequiredString(root, "category"),
            PackContentFields.ResourcePath(PackContentFields.RequiredString(root, "icon"), "icon"),
            tint is null ? null : PackContentFields.ResourcePath(tint, "tintIcon"),
            PackContentFields.Namespaced(PackContentFields.RequiredString(root, "leftBehavior"), "leftBehavior"),
            PackContentFields.Namespaced(PackContentFields.RequiredString(root, "rightBehavior"), "rightBehavior"),
            mining,
            maxStackSize,
            iconVariants.AsReadOnly());
    }
}
