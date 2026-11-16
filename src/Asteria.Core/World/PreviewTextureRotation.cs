namespace Asteria.Core.World;

/// <summary>
/// Rotate a decoded RGBA preview in the same direction as the terrain UV
/// sampling transform. The renderer owns PNG decoding/encoding; Core owns
/// pixel coordinate mapping. Rotations swap dimensions for nonsquare art.
/// </summary>
public static class PreviewTextureRotation
{
    public readonly record struct Result(int Width, int Height, byte[] Rgba);

    public static Result Rotate(
        ReadOnlySpan<byte> source, int width, int height,
        TextureRotation rotation)
    {
        if (width <= 0 || height <= 0 ||
            (long)width * height > 1024 * 1024 ||
            (long)width * height * 4 != source.Length)
            throw new ArgumentOutOfRangeException(nameof(source),
                "Preview must contain 1..1,048,576 RGBA pixels.");
        if (!Enum.IsDefined(rotation))
            throw new ArgumentOutOfRangeException(nameof(rotation));

        var quarterTurn = (int)rotation;
        var swap = quarterTurn is 1 or 3;
        var outputWidth = swap ? height : width;
        var outputHeight = swap ? width : height;
        var output = new byte[source.Length];
        for (var y = 0; y < outputHeight; y++)
        for (var x = 0; x < outputWidth; x++)
        {
            var (sx, sy) = rotation switch
            {
                TextureRotation.Degrees0 => (x, y),
                // Mesh UVs: 90° (1-v,u); 270° (v,1-u).
                TextureRotation.Degrees90 => (width - 1 - y, x),
                TextureRotation.Degrees180 => (width - 1 - x, height - 1 - y),
                TextureRotation.Degrees270 => (y, height - 1 - x),
                _ => throw new ArgumentOutOfRangeException(nameof(rotation))
            };
            var sourceIndex = (sy * width + sx) * 4;
            var targetIndex = (y * outputWidth + x) * 4;
            source.Slice(sourceIndex, 4).CopyTo(output.AsSpan(targetIndex, 4));
        }
        return new Result(outputWidth, outputHeight, output);
    }
}
