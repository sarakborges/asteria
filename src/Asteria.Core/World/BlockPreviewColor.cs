using System.Globalization;
using System.Numerics;

namespace Asteria.Core.World;

public readonly record struct BlockPreviewColor(byte Red, byte Green, byte Blue)
{
    public static BlockPreviewColor Missing => new(255, 0, 255);

    /// <summary>Convert normalized, finite biome sRGB channels into the
    /// preview compositor's 8-bit representation.</summary>
    public static BlockPreviewColor FromNormalized(Vector3 rgb)
    {
        if (!float.IsFinite(rgb.X) || !float.IsFinite(rgb.Y) ||
            !float.IsFinite(rgb.Z))
            throw new ArgumentOutOfRangeException(nameof(rgb));

        static byte Channel(float component) =>
            (byte)MathF.Round(Math.Clamp(component, 0f, 1f) * 255f);

        return new BlockPreviewColor(
            Channel(rgb.X), Channel(rgb.Y), Channel(rgb.Z));
    }

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
