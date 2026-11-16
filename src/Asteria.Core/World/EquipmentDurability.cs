namespace Asteria.Core.World;

/// <summary>
/// Reserved portable metadata for durability on single-item armor stacks.
/// A missing field means a pristine item, preserving legacy inventory saves.
/// </summary>
public static class EquipmentDurability
{
    public const string MetadataKey = "asteria:durability";

    public static int Remaining(InventoryEntry entry, int maximum)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (maximum is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(maximum));
        if (!entry.Metadata.TryGetValue(MetadataKey, out var text))
            return maximum;
        if (!int.TryParse(text, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var remaining) ||
            remaining < 1 || remaining > maximum)
            throw new InvalidDataException("Invalid equipment durability metadata.");
        return remaining;
    }
}
