using Asteria.Client.Content;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Rendering;

public sealed class TerrainTextureCatalog
{
    private const int FallbackSize = 16;

    private readonly System.Collections.Generic.Dictionary<string, int> _indices;

    private TerrainTextureCatalog(
        Texture2DArray textureArray,
        System.Collections.Generic.Dictionary<string, int> indices)
    {
        TextureArray = textureArray;
        _indices = indices;
    }

    public Texture2DArray TextureArray { get; }

    public int TextureCount => _indices.Count;

    public int GetIndex(string path) =>
        _indices.TryGetValue(path, out var index)
            ? index
            : throw new KeyNotFoundException($"Texture is not present in terrain array: {path}");

    public TerrainTextureLookup CreateLookup() =>
        new(_indices);

    public static TerrainTextureCatalog Create(
        BlockRegistry blocks,
        AttachedLayerRegistry layers,
        PackSelection selection)
    {
        ArgumentNullException.ThrowIfNull(blocks);
        ArgumentNullException.ThrowIfNull(layers);

        var paths = blocks
            .AuthoredDefinitions()
            .SelectMany(entry =>
                TextureLayers(
                    entry.Definition))
            .Select(layer => layer.Texture)
            .Concat(layers.Definitions.Select(layer => layer.Texture))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        var loaded = new List<(string Path, Image Image)>(paths.Length);
        foreach (var path in paths)
        {
            loaded.Add((path, LoadImage(selection, path)));
        }

        var width = loaded.Count > 0 ? loaded[0].Image.GetWidth() : FallbackSize;
        var height = loaded.Count > 0 ? loaded[0].Image.GetHeight() : FallbackSize;
        var images = new Godot.Collections.Array<Image>
        {
            CreateFallback(width, height),
        };
        var indices = new System.Collections.Generic.Dictionary<string, int>(
            paths.Length,
            StringComparer.Ordinal);

        for (var index = 0; index < loaded.Count; index++)
        {
            var (path, image) = loaded[index];
            Normalize(image, width, height, path);
            images.Add(image);
            indices.Add(path, index + 1);
        }

        var textureArray = new Texture2DArray();
        var error = textureArray.CreateFromImages(images);
        if (error != Error.Ok)
        {
            throw new InvalidOperationException($"Could not create terrain texture array: {error}");
        }

        GD.Print($"terrain textures: {indices.Count} authored layers + fallback, {width}x{height}");
        return new TerrainTextureCatalog(textureArray, indices);
    }

    private static IEnumerable<BlockTextureLayer>
        TextureLayers(
            BlockDefinition definition)
    {
        foreach (var layer in
                 definition.Textures.AllLayers())
        {
            yield return layer;
        }

        if (definition.Visual.Texture is
            { } visualTexture)
        {
            yield return visualTexture;
        }
    }

    private static Image LoadImage(
        PackSelection selection,
        string relativePath)
    {
        return ProjectPackFiles.LoadImage(
            selection,
            relativePath);
    }

    private static void Normalize(Image image, int width, int height, string path)
    {
        if (image.IsCompressed())
        {
            var error = image.Decompress();
            if (error != Error.Ok)
            {
                throw new InvalidOperationException($"Could not decompress block texture {path}: {error}");
            }
        }

        if (image.HasMipmaps())
        {
            image.ClearMipmaps();
        }

        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }

        if (image.GetWidth() != width || image.GetHeight() != height)
        {
            GD.PushWarning(
                $"Resizing block texture {path} from {image.GetWidth()}x{image.GetHeight()} " +
                $"to {width}x{height} for the terrain array.");
            image.Resize(width, height, Image.Interpolation.Nearest);
        }
    }

    private static Image CreateFallback(int width, int height)
    {
        var image = Image.CreateEmpty(width, height, false, Image.Format.Rgba8);
        var dark = new Color(0.12f, 0.02f, 0.12f);
        var bright = new Color(1f, 0f, 1f);

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                image.SetPixel(x, y, ((x / 4) + (y / 4)) % 2 == 0 ? bright : dark);
            }
        }

        return image;
    }
}
