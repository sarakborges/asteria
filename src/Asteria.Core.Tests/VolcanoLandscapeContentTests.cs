using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VolcanoLandscapeContentTests
{
    private static readonly string[] LivingTrees =
    [
        "cinderwood", "briarflame", "ashveil", "embercoil"
    ];

    [Fact]
    public void VolcanicTreeFamiliesHaveValidBasaltRootsAndFoliage()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        structures.ValidateBlocks(blocks);
        var dimension = DimensionRegistry.FromJson(ReadPackJson("dimensions"))
            .Get(DimensionId.Overworld);
        foreach (var species in LivingTrees)
        {
            var root = Assert.Single(dimension.GeneratedSurfaceStructures,
                r => r.Biome == "asteria:overworld/scorched_wastes" &&
                     r.Structure == "asteria:tree_" + species);
            Assert.True(root.HabitatWeights!.For("ash_aprons") >
                        root.HabitatWeights.For("hardened_highlands"));
            var trees = structures.ResolveReference("asteria:tree_" + species);
            Assert.Equal(2, trees.Count);
            Assert.All(trees, tree =>
            {
                Assert.True(tree.Restrictions.RequiresDryGround);
                Assert.Contains("asteria:basalt", tree.Restrictions.GroundBlocks);
                Assert.Equal(StructureFluidPolicy.Forbid, tree.Generation.FluidPolicy);
                Assert.Equal(StructureReplacePolicy.Terrain,
                    tree.Generation.ReplacePolicy);
                Assert.Contains(tree.Voxels,
                    v => v.Block == "asteria:log_" + species);
                Assert.Contains(tree.Voxels,
                    v => v.Block == "asteria:leaf_" + species);
            });
        }
    }

    [Fact]
    public void VolcanoDecoratorsFollowAshFlankAndHighlandHabitats()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var volcano = biomes.Get("asteria:overworld/scorched_wastes");
        foreach (var name in new[] {
            "thorn_ash", "bloom_cinder",
            "lichen_soot" })
        {
            var rule = Assert.Single(volcano.Decorations,
                d => d.Block == "asteria:" + name);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotNull(rule.Conditions);
            Assert.NotEmpty(rule.SurfaceBlocks);
            Assert.All(rule.SurfaceBlocks, b =>
                Assert.Contains(b, new[]
                { "asteria:basalt", "asteria:cobble_basalt", "asteria:gravel" }));
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);

            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            Assert.True(block.HasTag("support_below"));
            Assert.False(block.IsCollidable);
            Assert.False(block.CastsShadow);
            Assert.Equal(BlockRenderMode.Cutout, block.RenderMode);
        }
        var thorn = Assert.Single(volcano.Decorations,
            d => d.Block == "asteria:thorn_ash");
        Assert.True(thorn.HabitatWeights!.For("ash_aprons") >
                    thorn.HabitatWeights.For("hardened_highlands"));
        var soot = Assert.Single(volcano.Decorations,
            d => d.Block == "asteria:lichen_soot");
        Assert.True(soot.HabitatWeights!.For("fractured_flanks") >
                    soot.HabitatWeights.For("ash_aprons"));
    }

    [Fact]
    public void VolcanicTreesUseExistingSpeciesTextures()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        foreach (var species in LivingTrees)
        foreach (var prefix in new[] { "log", "leaf" })
        {
            var block = blocks.GetDefinition(
                blocks.GetId("asteria:" + prefix + "_" + species));
            Assert.All(block.Textures.AllLayers(), layer =>
                Assert.StartsWith("textures/blocks/trees/" + species + "/",
                    layer.Texture));
        }
    }

    private static IEnumerable<string> ReadPackJson(string folder)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", folder);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
