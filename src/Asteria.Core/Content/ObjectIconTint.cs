using Asteria.Core.World;

namespace Asteria.Core.Content;

/// <summary>
/// Portable bounded RGBA tint composition for icon assets. Color values use
/// the same 8-bit authored sRGB channel multiplication as the block preview;
/// alpha is never altered. No renderer, filesystem or mutable cache in Core.
/// </summary>
public static class ObjectIconTint
{
    public const int MaximumPixels = 1024 * 1024;

    public static byte[] Apply(ReadOnlySpan<byte> rgba, BlockPreviewColor tint)
    {
        if (rgba.Length == 0 || rgba.Length % 4 != 0 ||
            rgba.Length / 4 > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(rgba),
                "Object icon must contain 1..1,048,576 RGBA pixels.");
        var output = rgba.ToArray();
        for (var i = 0; i < output.Length; i += 4)
        {
            output[i] = Multiply(output[i], tint.Red);
            output[i + 1] = Multiply(output[i + 1], tint.Green);
            output[i + 2] = Multiply(output[i + 2], tint.Blue);
            // Alpha is left unchanged, including partially transparent edges.
        }
        return output;
    }

    private static byte Multiply(byte source, byte tint) =>
        (byte)((source * tint + 127) / 255);
}
