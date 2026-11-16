namespace Asteria.Core.World;

/// <summary>
/// Bounded RGBA source-over for preview-only attached layers. All calculations
/// are sRGB 8-bit, matching the existing block preview compositing convention.
/// The alpha-cutoff discards masked pixels but never erases pixels below them.
/// </summary>
public static class PreviewLayerBlend
{
    public const int MaximumPixels = 1024 * 1024;

    public static byte[] SourceOver(
        ReadOnlySpan<byte> baseRgba, ReadOnlySpan<byte> layerRgba,
        BlockPreviewColor tint, float? alphaCutoff = null)
    {
        if (baseRgba.Length == 0 || baseRgba.Length % 4 != 0 ||
            baseRgba.Length != layerRgba.Length ||
            baseRgba.Length / 4 > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(layerRgba),
                "Layer and host must contain the same bounded RGBA pixel count.");
        if (alphaCutoff is { } cutoff &&
            (!float.IsFinite(cutoff) || cutoff < 0f || cutoff > 1f))
            throw new ArgumentOutOfRangeException(nameof(alphaCutoff));

        var result = baseRgba.ToArray();
        for (var i = 0; i < result.Length; i += 4)
        {
            var frontAlpha = layerRgba[i + 3] / 255f;
            if (frontAlpha <= 0f ||
                (alphaCutoff is { } threshold && frontAlpha < threshold))
                continue;
            var behindAlpha = result[i + 3] / 255f;
            var outputAlpha = frontAlpha + behindAlpha * (1f - frontAlpha);
            for (var channel = 0; channel < 3; channel++)
            {
                var filter = channel switch
                {
                    0 => tint.Red,
                    1 => tint.Green,
                    _ => tint.Blue
                };
                var foreground = layerRgba[i + channel] * filter / 255f;
                var background = result[i + channel];
                result[i + channel] = ToByte(
                    (foreground * frontAlpha +
                     background * behindAlpha * (1f - frontAlpha)) /
                    outputAlpha);
            }
            result[i + 3] = ToByte(outputAlpha * 255f);
        }
        return result;
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value), 0, 255);
}
