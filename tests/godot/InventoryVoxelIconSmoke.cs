using Asteria.Client.Rendering;
using Asteria.Core.Content;
using Asteria.Core.World;
using Godot;

namespace Asteria.Client.Tests;

/// <summary>
/// Runs in the native Godot smoke to validate the production inventory/HUD
/// rasterizer with the actual pack grass textures and biome-color variants.
/// </summary>
public partial class InventoryVoxelIconSmoke : Node
{
    public bool Verify()
    {
        try
        {
            var path = ProjectSettings.GlobalizePath(
                "res://packs/default/data/blocks/block_grass.json");
            var block = BlockDefinitionJson.Parse(File.ReadAllText(path));
            var blocks = new BlockRegistry([block]);
            var layers = new AttachedLayerRegistry([]);
            var dyes = new DyeRegistry([]);
            var fluids = new FluidRegistry([]);
            var catalog = TerrainTextureCatalog.Create(
                blocks, layers, PackSelection.Default);
            var rasterizer = new InventoryVoxelIconRasterizer(
                blocks, fluids, layers, dyes, catalog.CreateLookup(), catalog);
            var snapshot = BlockStateSnapshot.FromCell(
                new VoxelCell(blocks.GetId(block.Id)));

            var authored = rasterizer.Render(block, snapshot);
            var green = rasterizer.Render(block, snapshot,
                new BlockPreviewColor(40, 210, 60));
            var red = rasterizer.Render(block, snapshot,
                new BlockPreviewColor(215, 45, 55));
            if (authored is null || green is null || red is null)
                throw new InvalidDataException("No canonical cube PNG was produced.");
            if (authored == green || red == green)
                throw new InvalidDataException("Biome tint did not change icon pixels.");
            if (!ReferenceEquals(green, rasterizer.Render(
                block, snapshot, new BlockPreviewColor(40, 210, 60))))
                throw new InvalidDataException(
                    "Identical sampled biome tint did not reuse its icon.");

            const string prefix = "data:image/png;base64,";
            if (!green.StartsWith(prefix, StringComparison.Ordinal))
                throw new InvalidDataException("Icon is not a PNG data URL.");
            using var image = new Image();
            if (image.LoadPngFromBuffer(Convert.FromBase64String(
                green[prefix.Length..])) != Error.Ok ||
                image.GetSize() != new Vector2I(96, 96))
                throw new InvalidDataException("Canonical cube PNG is unreadable.");
            var visible = false;
            for (var y = 0; y < image.GetHeight() && !visible; y++)
            for (var x = 0; x < image.GetWidth() && !visible; x++)
                visible = image.GetPixel(x, y).A > .5f;
            if (!visible)
                throw new InvalidDataException("Canonical cube PNG is transparent.");

            VerifyPlayerBiomeTint();
            VerifyLayerIconTint();
            GD.Print("Native voxel icon smoke: geometry, tint, cache, PNG, biome, Layer OK");
            return true;
        }
        catch (Exception error)
        {
            GD.PushError("Native voxel icon smoke failed: " + error);
            return false;
        }
    }

    private static void VerifyLayerIconTint()
    {
        var selection = PackSelection.Default;
        AttachedLayerDefinition ReadLayer(string name) =>
            AttachedLayerDefinition.Parse(File.ReadAllText(
                ProjectSettings.GlobalizePath(
                    $"res://packs/default/data/layers/{name}.json")));

        var renderer = new InventoryBlockPreviewRenderer(selection);
        var foliage = ReadLayer("layer_foliage");
        var moss = ReadLayer("moss");
        var plain = ReadLayer("lichen_cliff");
        var greenTint = new BlockPreviewColor(30, 210, 55);
        var redTint = new BlockPreviewColor(210, 45, 60);
        if (foliage.Tint != BlockTint.Foliage ||
            moss.Tint != BlockTint.Foliage ||
            plain.Tint != BlockTint.None)
            throw new InvalidDataException("Authored layer tint fixture changed.");

        var green = renderer.LayerDataUri(foliage, greenTint);
        var red = renderer.LayerDataUri(foliage, redTint);
        if (green == red || !ReferenceEquals(green,
            renderer.LayerDataUri(foliage, greenTint)))
            throw new InvalidDataException(
                "Layer tint colors or shared color cache are incorrect.");
        if (renderer.LayerDataUri(moss, greenTint) is not { Length: > 40 })
            throw new InvalidDataException("Moss Layer icon could not be tinted.");
        try
        {
            renderer.LayerDataUri(plain, greenTint);
            throw new InvalidDataException("Untinted layer should retain raw pack URL.");
        }
        catch (ArgumentException) { }

        const string prefix = "data:image/png;base64,";
        if (!green.StartsWith(prefix, StringComparison.Ordinal))
            throw new InvalidDataException("Tinted layer icon must be PNG.");
        using var original = Asteria.Client.Content.ProjectPackFiles.LoadImage(
            selection, foliage.Texture);
        if (original.IsCompressed()) original.Decompress();
        if (original.GetFormat() != Image.Format.Rgba8)
            original.Convert(Image.Format.Rgba8);
        using var icon = new Image();
        if (icon.LoadPngFromBuffer(
                Convert.FromBase64String(green[prefix.Length..])) != Error.Ok ||
            icon.GetSize() != original.GetSize())
            throw new InvalidDataException("Tinted layer PNG is unreadable.");

        var colored = false;
        for (var y = 0; y < icon.GetHeight(); y++)
        for (var x = 0; x < icon.GetWidth(); x++)
        {
            var source = original.GetPixel(x, y);
            var result = icon.GetPixel(x, y);
            if (Math.Abs(source.A - result.A) > 1f / 255f)
                throw new InvalidDataException(
                    "Layer tint must preserve transparent edges.");
            if (source.A > .5f &&
                (Math.Abs(source.R - result.R) > 2f / 255f ||
                 Math.Abs(source.G - result.G) > 2f / 255f ||
                 Math.Abs(source.B - result.B) > 2f / 255f))
                colored = true;
        }
        if (!colored)
            throw new InvalidDataException("Biome tint did not color Layer pixels.");
    }

    private static void VerifyPlayerBiomeTint()
    {
        var biome = new BiomeDefinition(
            "asteria:test/preview_biome",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0, 0, 64, 0, 32),
            [new BiomeSurfaceLayerDefinition("asteria:block_grass")],
            tints: new BiomeTintPaletteDefinition(
                grass: new BiomeTintColor(32, 190, 57)));
        var biomes = new BiomeRegistry([biome]);
        var field = new BiomeTintField(
            new BiomeField(123UL, [biome.Id], biomes), [biome]);
        var sampler = new InventoryIconTintSampler();
        var position = new Vector3(0.5f, 42f, 0.5f);
        if (!sampler.Sync(field, position) ||
            sampler.Sync(field, position) ||
            sampler.Sync(field, position + new Vector3(1, 0, 0)) ||
            sampler.Palette?.Grass != new BlockPreviewColor(32, 190, 57) ||
            sampler.Resolve(BlockTint.Grass, BlockPreviewColor.Missing)
                != new BlockPreviewColor(32, 190, 57) ||
            sampler.Resolve(BlockTint.None, BlockPreviewColor.Missing) is not null)
            throw new InvalidDataException("Invalid player biome icon tint.");

        sampler.Reset();
        if (sampler.Palette is not null ||
            sampler.Resolve(BlockTint.Grass, BlockPreviewColor.Missing) is not null ||
            !sampler.Sync(field, position))
            throw new InvalidDataException("Dimension change retained stale icon tint.");
    }
}
