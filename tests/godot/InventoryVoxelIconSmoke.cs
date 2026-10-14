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

            GD.Print("Native voxel icon smoke: geometry, tint, cache, PNG OK");
            return true;
        }
        catch (Exception error)
        {
            GD.PushError("Native voxel icon smoke failed: " + error);
            return false;
        }
    }
}
