using Asteria.Client.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

/// <summary>
/// Builds the same ordered, dyable texture layers used by terrain faces into
/// compact, cached PNG previews for the WebUI. The preview color is the authored
/// fallback tint, since an inventory item has no world biome position.
/// </summary>
public sealed class InventoryBlockPreviewRenderer
{
    private readonly PackSelection _pack;
    private readonly Dictionary<(string BlockId, BlockFace Face), string> _cache = [];

    public InventoryBlockPreviewRenderer(PackSelection pack)
    {
        _pack = pack;
    }

    public string? FaceDataUri(BlockDefinition block, BlockFace face)
    {
        ArgumentNullException.ThrowIfNull(block);
        var layers = block.Visual.Texture is { } sprite
            ? new[] { sprite }
            : block.Textures.ResolveForFace(face);
        if (layers.Count == 0)
            return null;

        var key = (block.Id, face);
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        using var first = ProjectPackFiles.LoadImage(_pack, layers[0].Texture);
        Normalize(first);
        var composite = Image.CreateEmpty(
            first.GetWidth(), first.GetHeight(), false, Image.Format.Rgba8);
        using (composite)
        {
            foreach (var layer in layers)
            {
                using var source = ProjectPackFiles.LoadImage(_pack, layer.Texture);
                Normalize(source);
                if (source.GetWidth() != composite.GetWidth() ||
                    source.GetHeight() != composite.GetHeight())
                    source.Resize(composite.GetWidth(), composite.GetHeight(),
                        Image.Interpolation.Nearest);

                var tinted = block.Tint != BlockTint.None && layer.Dyable;
                var tint = block.PreviewColor;
                for (var y = 0; y < composite.GetHeight(); y++)
                for (var x = 0; x < composite.GetWidth(); x++)
                {
                    var sourcePixel = source.GetPixel(x, y);
                    if (sourcePixel.A <= 0f) continue;
                    if (tinted)
                    {
                        sourcePixel.R *= tint.Red / 255f;
                        sourcePixel.G *= tint.Green / 255f;
                        sourcePixel.B *= tint.Blue / 255f;
                    }

                    var below = composite.GetPixel(x, y);
                    var alpha = sourcePixel.A +
                        below.A * (1f - sourcePixel.A);
                    var result = new Color(
                        (sourcePixel.R * sourcePixel.A +
                         below.R * below.A * (1f - sourcePixel.A)) / alpha,
                        (sourcePixel.G * sourcePixel.A +
                         below.G * below.A * (1f - sourcePixel.A)) / alpha,
                        (sourcePixel.B * sourcePixel.A +
                         below.B * below.A * (1f - sourcePixel.A)) / alpha,
                        alpha);
                    composite.SetPixel(x, y, result);
                }
            }

            var png = composite.SavePngToBuffer();
            var data = "data:image/png;base64," + Convert.ToBase64String(png);
            _cache.Add(key, data);
            return data;
        }
    }

    private static void Normalize(Image image)
    {
        if (image.IsCompressed())
        {
            var error = image.Decompress();
            if (error != Error.Ok)
                throw new InvalidDataException(
                    $"Cannot decompress preview texture: {error}");
        }

        if (image.HasMipmaps()) image.ClearMipmaps();
        if (image.GetFormat() != Image.Format.Rgba8)
            image.Convert(Image.Format.Rgba8);
    }
}
