using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VolcanoLandscapeContentTests
{
    [Fact]
    public void ScorchedWastesUsesOnlyRetainedBasaltDecorations()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, definition => definition.Id);
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);
        var biome = biomes.Get("asteria:overworld/scorched_wastes");

        Assert.Equal(
            ["ash_aprons", "fractured_flanks", "hardened_highlands"],
            biome.SurfaceHabitats!.Bands.Select(band => band.Id));
        Assert.Equal(2, biome.Decorations.Count);

        var pebble = Assert.Single(biome.Decorations,
            decoration => decoration.Block == "asteria:pebble");
        Assert.True(pebble.IsObject);
        Assert.All(pebble.SurfaceBlocks, support =>
            Assert.Contains(support,
                new[] { "asteria:basalt", "asteria:cobble_basalt", "asteria:gravel" }));
        Assert.True(pebble.HabitatWeights!.For("fractured_flanks") >
                    pebble.HabitatWeights.For("ash_aprons"));
        Assert.True(objects.Get(pebble.Block).Supports(BlockFace.Top));

        var lichen = Assert.Single(biome.Decorations,
            decoration => decoration.Block == "asteria:lichen_soot");
        Assert.False(lichen.IsObject);
        Assert.All(lichen.SurfaceBlocks, support =>
            Assert.Contains(support,
                new[] { "asteria:basalt", "asteria:cobble_basalt" }));
        Assert.True(lichen.HabitatWeights!.For("fractured_flanks") >
                    lichen.HabitatWeights.For("ash_aprons"));
        var definition = blocks.GetDefinition(blocks.GetId(lichen.Block));
        Assert.Equal(BlockVisualKind.GroundSprite, definition.Visual.Kind);
        Assert.False(definition.IsCollidable);
        Assert.Equal(BlockRenderMode.Cutout, definition.RenderMode);
        Assert.Equal("textures/layers/lichen_soot.png",
            definition.Visual.Texture!.Value.Texture);
    }

    [Fact]
    public void DeletedVolcanicTreesAreAbsentButCalderaRemains()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        structures.ValidateBlocks(blocks);
        var dimension = DimensionRegistry.FromJson(ReadPackJson("dimensions"))
            .Get(DimensionId.Overworld);
        var roots = dimension.GeneratedSurfaceStructures
            .Where(rule => rule.Biome == "asteria:overworld/scorched_wastes")
            .ToArray();

        Assert.Equal(4, roots.Length);
        Assert.Equal(
            new[] { "asteria:boulder_basalt", "asteria:cluster_basalt", "asteria:outcrop_basalt", "asteria:volcano" },
            roots.Select(root => root.Structure).OrderBy(id => id, StringComparer.Ordinal));
        Assert.DoesNotContain(roots,
            root => root.Structure.StartsWith("asteria:tree_", StringComparison.Ordinal));
        Assert.True(structures.ResolvesReference("asteria:volcano"));

        foreach (var species in new[] { "cinderwood", "briarflame", "ashveil", "embercoil" })
        {
            Assert.False(blocks.TryGetId("asteria:log_" + species, out _));
            Assert.False(blocks.TryGetId("asteria:leaf_" + species, out _));
            Assert.Throws<KeyNotFoundException>(() =>
                structures.ResolveReference("asteria:tree_" + species));
        }
    }

    private static IEnumerable<string> ReadPackJson(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
