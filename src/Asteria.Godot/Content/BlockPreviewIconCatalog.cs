using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Content;

/// <summary>
/// Produces pack-scoped, alpha-composited block face icons for WebUI.
/// The terrain and preview use the same authored face layers and tint flags.
/// </summary>
internal sealed class BlockPreviewIconCatalog
{
    private const int MaxPngBytes = 131072;

    private readonly PackSelection _pack;
    private readonly Func<string, string> _plainIconDataUri;
    private readonly Dictionary<(string BlockId, BlockFace Face), string> _cache = new();

    public BlockPreviewIconCatalog(
        PackSelection pack,
        Func<string, string> plainIconDataUri)
    {
        _pack = pack;
        _plainIconDataUri = plainIconDataUri ??
            throw new ArgumentNullException(nameof(plainIconDataUri));
    }

    public string? ResolveFace(BlockDefinition block, BlockFace face)
    {
        ArgumentNullException.ThrowIfNull(block);

        var layers = block.Textures.ResolveForFace(face);
        if (layers.Count == 0)
            return null;

        // Keep preview constraints aligned with the terrain material encoder.
        if (layers.Count > 2)
            throw new InvalidOperationException(
                $"Block preview supports at most two texture layers per face: {block.Id} {face}.");

        var key = (block.Id, face);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var result = layers.Count == 1 && !ShouldTint(block, layers[0])
            ? _plainIconDataUri(layers[0].Texture)
            : Compose(block, layers);

        _cache.Add(key, result);
        return result;
    }

    private string Compose(
        BlockDefinition block,
        IReadOnlyList<BlockTextureLayer> layers)
    {
        Image? composed = null;
        foreach (var layer in layers)
        {
            var image = ProjectPackFiles.LoadImage(_pack, layer.Texture);
            Normalize(image);

            if (composed is null)
            {
                composed = image;
            }
            else
            {
                if (image.GetWidth() != composed.GetWidth() ||
                    image.GetHeight() != composed.GetHeight())
                {
                    image.Resize(
                        composed.GetWidth(), composed.GetHeight(),
                        Image.Interpolation.Nearest);
                }
            }

            if (ShouldTint(block, layer))
                ApplyPreviewTint(image, block.PreviewColor);

            if (!ReferenceEquals(composed, image))
            {
                composed!.BlendRect(
                    image,
                    new Rect2I(0, 0, image.GetWidth(), image.GetHeight()),
                    Vector2I.Zero);
            }
        }

        var png = composed!.SavePngToBuffer();
        if (png.Length is < 1 or > MaxPngBytes)
            throw new InvalidDataException(
                $"Composed block preview exceeds allowed size: {block.Id}");

        return "data:image/png;base64," + Convert.ToBase64String(png);
    }

    private static bool ShouldTint(
        BlockDefinition block,
        BlockTextureLayer layer) =>
        layer.Dyable && block.Tint != BlockTint.None;

    private static void Normalize(Image image)
    {
        if (image.IsCompressed() && image.Decompress() != Error.Ok)
            throw new InvalidDataException("Block preview image could not be decompressed.");

        if (image.HasMipmaps())
            image.ClearMipmaps();

        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);
    }

    private static void ApplyPreviewTint(Image image, BlockPreviewColor tint)
    {
        var red = tint.Red / 255f;
        var green = tint.Green / 255f;
        var blue = tint.Blue / 255f;

        for (var y = 0; y < image.GetHeight(); y++)
        for (var x = 0; x < image.GetWidth(); x++)
        {
            var pixel = image.GetPixel(x, y);
            image.SetPixel(
                x, y,
                new Color(
                    pixel.R * red,
                    pixel.G * green,
                    pixel.B * blue,
                    pixel.A));
        }
    }
}
