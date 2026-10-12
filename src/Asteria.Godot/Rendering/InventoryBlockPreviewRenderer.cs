using Asteria.Client.Content;
using Asteria.Core.Content;
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
    private readonly Dictionary<
        (string BlockId, BlockFace SourceFace, TextureRotation Rotation), string> _cache = [];
    // Bounded icon-color variants; keys contain authored IDs and 8-bit color.
    private readonly Dictionary<(string ObjectId, BlockPreviewColor Tint), string> _objectIcons = [];
    private const int MaximumObjectIconVariants = 256;

    public InventoryBlockPreviewRenderer(PackSelection pack)
    {
        _pack = pack;
    }

    public string? FaceDataUri(
        BlockDefinition block, BlockFace sourceFace,
        TextureRotation rotation = TextureRotation.Degrees0,
        BlockPreviewColor? sampledTint = null)
    {
        ArgumentNullException.ThrowIfNull(block);
        if (!Enum.IsDefined(rotation))
            throw new ArgumentOutOfRangeException(nameof(rotation));
        var layers = block.Visual.Texture is { } sprite
            ? new[] { sprite }
            : block.Textures.ResolveForFace(sourceFace);
        if (layers.Count == 0)
            return null;

        var key = (block.Id, sourceFace, rotation);
        // World biome colors are spatially derived and may change across
        // Sphere transitions; only the timeless authored fallback is cached.
        if (sampledTint is null && _cache.TryGetValue(key, out var cached))
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
                var tint = sampledTint ?? block.PreviewColor;
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

            // World meshes apply BlockUvRotation to the sampled coordinates.
            // The browser preview has no shader UV input: rotate the pixels
            // by precisely the inverse lookup instead, preserving alpha.
            byte[] png;
            if (rotation == TextureRotation.Degrees0)
            {
                png = composite.SavePngToBuffer();
            }
            else
            {
                var rotated = PreviewTextureRotation.Rotate(
                    composite.GetData(), composite.GetWidth(),
                    composite.GetHeight(), rotation);
                using var image = Image.CreateFromData(
                    rotated.Width, rotated.Height, false,
                    Image.Format.Rgba8, rotated.Rgba);
                png = image.SavePngToBuffer();
            }

            var data = "data:image/png;base64," + Convert.ToBase64String(png);
            if (sampledTint is null) _cache.Add(key, data);
            return data;
        }
    }

    /// <summary>
    /// The HUD's actual voxel may carry removable layers on any face.
    /// Compose those layers in saved attachment order over the base face,
    /// applying the same authored UV rotation, color channel and alpha
    /// cutoff as terrain. Unlike inventory icons this path is not cached:
    /// target surfaces and sampled biome tints are mutable world state.
    /// </summary>
    public string? LayeredFaceDataUri(
        BlockDefinition block, BlockFace sourceFace, TextureRotation baseRotation,
        IReadOnlyList<PreviewAttachedLayer> attachments,
        Func<BlockTint, BlockPreviewColor> resolveTint,
        BlockPreviewColor? sampledBaseTint = null)
    {
        ArgumentNullException.ThrowIfNull(attachments);
        ArgumentNullException.ThrowIfNull(resolveTint);
        var baseUri = FaceDataUri(
            block, sourceFace, baseRotation, sampledBaseTint);
        if (baseUri is null || attachments.Count == 0) return baseUri;

        const string pngPrefix = "data:image/png;base64,";
        using var composite = Image.CreateEmpty(1, 1, false, Image.Format.Rgba8);
        var read = composite.LoadPngFromBuffer(
            Convert.FromBase64String(baseUri[pngPrefix.Length..]));
        if (read != Error.Ok)
            throw new InvalidDataException(
                $"Could not decode block preview for {block.Id}: {read}");
        Normalize(composite);
        var width = composite.GetWidth();
        var height = composite.GetHeight();
        var pixels = composite.GetData();

        foreach (var attachment in attachments)
        {
            using var source = ProjectPackFiles.LoadImage(_pack, attachment.Texture);
            Normalize(source);
            var rotated = PreviewTextureRotation.Rotate(
                source.GetData(), source.GetWidth(), source.GetHeight(),
                attachment.Rotation);
            using var layerImage = Image.CreateFromData(
                rotated.Width, rotated.Height, false, Image.Format.Rgba8,
                rotated.Rgba);
            if (layerImage.GetWidth() != width || layerImage.GetHeight() != height)
                layerImage.Resize(width, height, Image.Interpolation.Nearest);
            var tint = attachment.Tint == BlockTint.None
                ? new BlockPreviewColor(255, 255, 255)
                : resolveTint(attachment.Tint);
            pixels = PreviewLayerBlend.SourceOver(
                pixels, layerImage.GetData(), tint, attachment.AlphaCutoff);
        }

        using var output = Image.CreateFromData(
            width, height, false, Image.Format.Rgba8, pixels);
        return pngPrefix + Convert.ToBase64String(output.SavePngToBuffer());
    }

    /// <summary>
    /// Produces authored fallback-tinted Object artwork for inventory or
    /// sampled-biome-tinted artwork for a world target. White/untinted Objects
    /// keep their existing raw pack PNG path in the caller.
    /// </summary>
    public string ObjectDataUri(
        ObjectDefinition definition, BlockPreviewColor? sampledTint = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Tint == BlockTint.None)
            throw new ArgumentException(
                "Untinted Object icons must use the authored source PNG.",
                nameof(definition));

        var tint = sampledTint ?? definition.TintFallback;
        var key = (definition.Id, tint);
        if (_objectIcons.TryGetValue(key, out var cached))
            return cached;

        using var image = ProjectPackFiles.LoadImage(_pack, definition.Icon);
        Normalize(image);
        if (image.GetWidth() <= 0 || image.GetHeight() <= 0 ||
            (long)image.GetWidth() * image.GetHeight() >
            ObjectIconTint.MaximumPixels)
            throw new InvalidDataException(
                $"Object icon exceeds maximum decoded size: {definition.Id}");
        var pixels = ObjectIconTint.Apply(image.GetData(), tint);
        using var tinted = Image.CreateFromData(
            image.GetWidth(), image.GetHeight(), false, Image.Format.Rgba8, pixels);
        var uri = "data:image/png;base64," +
            Convert.ToBase64String(tinted.SavePngToBuffer());
        if (_objectIcons.Count >= MaximumObjectIconVariants)
            _objectIcons.Clear();
        _objectIcons.Add(key, uri);
        return uri;
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
