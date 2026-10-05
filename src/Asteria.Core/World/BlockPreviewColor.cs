using System.Globalization;

namespace Asteria.Core.World;

public readonly record struct BlockPreviewColor(byte Red, byte Green, byte Blue)
{
    public static BlockPreviewColor Missing => new(255, 0, 255);

    public static BlockPreviewColor Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        var hex = value.StartsWith('#') ? value[1..] : value;
        if (hex.Length != 6 ||
            !byte.TryParse(hex.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
            !byte.TryParse(hex.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
            !byte.TryParse(hex.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
        {
            throw new FormatException($"Block preview color must be a six-digit RGB hex value: {value}");
        }

        return new BlockPreviewColor(red, green, blue);
    }
}
