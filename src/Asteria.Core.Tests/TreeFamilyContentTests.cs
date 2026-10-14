using System.Buffers.Binary;
using System.Text.Json;
using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class TreeFamilyContentTests
{
    private static string PackRoot => Path.Combine(AppContext.BaseDirectory, "packs", "default");

    public static IEnumerable<object[]> Families() =>
        Directory.EnumerateDirectories(Path.Combine(PackRoot, "resources", "textures", "blocks", "trees"))
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => new object[] { Path.GetFileName(path) });

    [Theory]
    [MemberData(nameof(Families))]
    public void EveryTreeHasUsableWoodVariantsPlanksLeavesAndSapling(string species)
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var natural = blocks.GetId("asteria:log_" + species);
        foreach (var key in new[] { "natural", "stripped", "hollow", "stripped_hollow" })
        {
            Assert.True(blocks.TryGetVariant(natural, key, out var variant),
                $"{species} must support {key} through the existing tool variant lookup.");
            var definition = blocks.GetDefinition(variant);
            Assert.Equal("blocks_wooden", definition.Category);
            Assert.Equal("asteria:log_" + species, definition.Variant!.Family);
            Assert.Contains(BlockOrientation.X, definition.Orientations);
            Assert.Contains(BlockOrientation.Y, definition.Orientations);
            Assert.Contains(BlockOrientation.Z, definition.Orientations);
            Assert.Equal(key.Contains("hollow", StringComparison.Ordinal)
                ? BlockShapeKind.Hollow : BlockShapeKind.Cube, definition.Shape.Kind);
            var expectedTop = key.Contains("stripped", StringComparison.Ordinal)
                ? "top_stripped" : "top";
            Assert.All(definition.Textures.ResolveForFace(BlockFace.Top), layer =>
                Assert.Equal($"textures/blocks/trees/{species}/{expectedTop}.png", layer.Texture));
            Assert.All(definition.Textures.ResolveForFace(BlockFace.Bottom), layer =>
                Assert.Equal($"textures/blocks/trees/{species}/{expectedTop}.png", layer.Texture));
            Assert.True(definition.Mining.RequiredTools.Contains("hatchet") ||
                        definition.Mining.PreferredTools.Contains("hatchet"));
        }

        var planks = blocks.GetDefinition(blocks.GetId("asteria:planks_" + species));
        Assert.Equal("blocks_wooden", planks.Category);
        Assert.Equal(BlockShapeKind.Cube, planks.Shape.Kind);
        Assert.All(planks.Textures.AllLayers(), layer =>
            Assert.Equal($"textures/blocks/trees/{species}/planks.png", layer.Texture));
        Assert.True(blocks.TryGetId("asteria:leaf_" + species, out _));

        var sapling = ObjectDefinition.Parse(File.ReadAllText(
            Path.Combine(PackRoot, "data", "objects", $"sapling_{species}.json")));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        var grown = structures.ResolveReference(sapling.GrowthStructure!);
        Assert.NotEmpty(grown);
        foreach (var structure in grown)
        {
            var ids = structure.Voxels.Select(voxel => voxel.Block).Distinct().ToArray();
            Assert.Contains("asteria:log_" + species, ids);
            var leaves = ids.Where(id => id.StartsWith("asteria:leaf_", StringComparison.Ordinal)).ToArray();
            Assert.NotEmpty(leaves);
            Assert.All(leaves, id => Assert.True(
                id == "asteria:leaf_" + species ||
                id.StartsWith("asteria:leaf_" + species + "_", StringComparison.Ordinal),
                $"{sapling.Id} grows foreign foliage {id} in {structure.Id}."));
        }
    }

    [Fact]
    public void EveryTreeBlockHasLocalizedNamesAndLeavesNameTheirFamily()
    {
        var registry = BlockRegistry.FromJson(ReadJson("blocks"));
        var definitions = ReadJson("blocks").Select(BlockDefinitionJson.Parse)
            .Where(block => block.Textures.AllLayers().Any(layer =>
                layer.Texture.StartsWith("textures/blocks/trees/", StringComparison.Ordinal)))
            .ToArray();
        foreach (var language in new[] { "english", "portuguese_brazil", "spanish" })
        {
            using var localization = JsonDocument.Parse(File.ReadAllText(Path.Combine(
                PackRoot, "data", "localization", language, "blocks.json")));
            foreach (var definition in definitions)
            {
                Assert.True(localization.RootElement.TryGetProperty(definition.Id, out var translated),
                    $"{language} is missing {definition.Id}.");
                var name = translated.GetProperty("/name").GetString();
                Assert.False(string.IsNullOrWhiteSpace(name));
            }
        }

        using var english = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            PackRoot, "data", "localization", "english", "blocks.json")));
        foreach (var leaf in definitions.Where(block => block.Id.StartsWith("asteria:leaf_", StringComparison.Ordinal)))
        {
            var path = leaf.Textures.AllLayers().First().Texture;
            var species = path.Split('/')[3];
            Assert.True(registry.TryGetId("asteria:log_" + species, out _),
                $"{leaf.Id} must have a matching tree wood family.");
            Assert.True(registry.TryGetId("asteria:planks_" + species, out _));
            var familyName = species == "frost_wood" ? "Frostwood" :
                string.Join(" ", species.Split('_').Select(word => char.ToUpperInvariant(word[0]) + word[1..]));
            var name = english.RootElement.GetProperty(leaf.Id).GetProperty("/name").GetString()!;
            Assert.Contains(familyName, name, StringComparison.Ordinal);
            Assert.EndsWith(" Leaves", name);
        }
    }

    [Theory]
    [MemberData(nameof(Families))]
    public void TreeTilesKeepThe64PixelAssetContract(string species)
    {
        foreach (var asset in new[] { "side", "stripped", "top", "top_stripped", "planks", "leaf", "sapling" })
        {
            var png = File.ReadAllBytes(Path.Combine(PackRoot, "resources", "textures", "blocks",
                "trees", species, asset + ".png"));
            Assert.True(png.Length >= 33);
            Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, png[..8]);
            Assert.Equal(64u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16, 4)));
            Assert.Equal(64u, BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20, 4)));
        }
    }

    private static IEnumerable<string> ReadJson(string domain) =>
        Directory.EnumerateFiles(Path.Combine(PackRoot, "data", domain), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal).Select(File.ReadAllText);
}
