using System.Text.Json;
using Asteria.Core.World;

namespace Asteria.Core.Content;

public sealed record ItemIconVariant(
    string MetadataKey,
    string MetadataValue,
    string Icon);

public sealed record ItemDefinition(
    string Id,
    string Category,
    string Icon,
    IReadOnlyList<ItemIconVariant> IconVariants,
    int MaxStackSize = 64,
    EquipmentSlot? EquipmentSlot = null,
    EquipmentVisualDefinition? EquipmentVisual = null,
    float DamageReduction = 0f)
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

        var maxStackSize = root.TryGetProperty("maxStackSize", out _)
            ? PackContentFields.PositiveInt(root, "maxStackSize")
            : 64;
        if (maxStackSize > 64)
            throw new FormatException("maxStackSize must not exceed 64");

        EquipmentSlot? equipmentSlot = null;
        if (root.TryGetProperty("equipmentSlot", out var equipmentValue))
        {
            if (equipmentValue.ValueKind != JsonValueKind.String ||
                !Enum.TryParse<EquipmentSlot>(equipmentValue.GetString(),
                    ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed))
                throw new FormatException("equipmentSlot must name a valid body slot.");
            if (maxStackSize != 1)
                throw new FormatException("Equippable items must have maxStackSize 1.");
            equipmentSlot = parsed;
        }

        EquipmentVisualDefinition? appearance = null;
        if (root.TryGetProperty("equipmentVisual", out var visual))
        {
            if (equipmentSlot is not { } slot)
                throw new FormatException(
                    "equipmentVisual requires an authored equipmentSlot.");
            appearance = EquipmentVisualDefinition.Parse(visual, slot);
        }

        var damageReduction = root.TryGetProperty("damageReduction", out var defense)
            ? defense.ValueKind == JsonValueKind.Number && defense.TryGetSingle(out var amount)
                && float.IsFinite(amount) && amount is >= 0f and <= 0.5f
                    ? amount
                    : throw new FormatException("damageReduction must be between 0 and 0.5.")
            : 0f;
        if (damageReduction > 0f && equipmentSlot is null)
            throw new FormatException("damageReduction requires equipmentSlot.");

        return new ItemDefinition(
            PackContentFields.Id(root),
            PackContentFields.RequiredString(root, "category"),
            PackContentFields.ResourcePath(PackContentFields.RequiredString(root, "icon"), "icon"),
            variants.AsReadOnly(),
            maxStackSize,
            equipmentSlot,
            appearance,
            damageReduction);
    }
}
