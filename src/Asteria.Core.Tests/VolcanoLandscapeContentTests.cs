using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VolcanoLandscapeContentTests
{
    private static readonly (string Name, int Variants, string Preferred,
        string Avoided)[] Families =
    [
        ("tree_charred_volcanic", 4, "ash_aprons", "hardened_highlands"),
        ("thicket_briar_volcanic", 3, "ash_aprons", "hardened_highlands"),
    ];

    [Fact]
    public void VolcanoStructureFamiliesUseBasaltGroundAndExcludeLava()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        structures.ValidateBlocks(blocks);
        var ocean = DimensionRegistry.FromJson(ReadPackJson("dimensions"))
            .Get(DimensionId.Overworld);
        var volcano = BiomeRegistry.FromJson(ReadPackJson("biomes"))
            .Get("asteria:overworld/scorched_wastes");

        Assert.Equal("asteria:basalt", volcano.SurfaceLayers[0].Block);
        Assert.Equal(7, Families.Sum(f => f.Variants));

        foreach (var (group, count, favored, avoided) in Families)
        {
            var rule = Assert.Single(ocean.GeneratedSurfaceStructures,
                r => r.Biome == volcano.Id &&
                     r.Structure == "asteria:" + group);
            Assert.NotNull(rule.HabitatWeights);
            Assert.True(rule.HabitatWeights!.For(favored) >
                        rule.HabitatWeights.For(avoided));

            var variants = structures.ResolveReference("asteria:" + group);
            Assert.Equal(count, variants.Count);
            Assert.All(variants, variant =>
            {
                Assert.True(variant.Rotation);
                Assert.True(variant.Restrictions.RequiresDryGround);
                Assert.Equal(1f, variant.Restrictions.RequiredBiomeCoverage);
                Assert.Equal(["asteria:basalt"],
                    variant.Restrictions.GroundBlocks);
                Assert.Equal(StructureFluidPolicy.Forbid,
                    variant.Generation.FluidPolicy);
                Assert.Equal(StructureReplacePolicy.Terrain,
                    variant.Generation.ReplacePolicy);
                Assert.InRange(variant.MaximumY, 3, 12);
                Assert.True(variant.Voxels.Count > 8);

                var ground = variant.Voxels.Where(voxel => voxel.Y == 0)
                    .ToDictionary(voxel => (voxel.X, voxel.Z), voxel => voxel.Block);
                Assert.NotEmpty(ground);
                Assert.All(ground.Values,
                    b => Assert.Equal("asteria:basalt", b));

                var stemBlocks = new[] { "asteria:log_charwood", "asteria:basalt" };
                foreach (var stem in variant.Voxels.Where(voxel =>
                    voxel.Y == 1 && stemBlocks.Contains(voxel.Block)))
                    Assert.True(ground.ContainsKey((stem.X, stem.Z)),
                        $"Unsupported base in {variant.Id}");
            });
        }
    }

    [Fact]
    public void VolcanoCharredTreesHaveDistinctSilhouettes()
    {
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        var trees = structures.ResolveReference("asteria:tree_charred_volcanic");
        Assert.True(trees.Max(t => t.MaximumY) >= 11);
        Assert.True(trees.Select(t => t.MaximumY).Distinct().Count() >= 3);
        Assert.All(trees, tree => Assert.Contains(tree.Voxels,
            voxel => voxel.Block == "asteria:log_charwood" &&
                     voxel.X != 0));

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
    public void VolcanoDecorationsKeepOwnTexturesAndLegacyWoodUsesOakFallback()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        foreach (var id in new[] {
            "log_charwood", "leaf_charwood_ember",
 })
        {
            var block = blocks.GetDefinition(blocks.GetId("asteria:" + id));
            Assert.True(block.IsCollidable);
            Assert.All(block.Textures.AllLayers(), tex =>
                Assert.StartsWith(id is "log_charwood" or "leaf_charwood_ember"
                    ? "textures/blocks/trees/oak/"
                    : "textures/blocks/" + id + ".png", tex.Texture));
        }
        var bloom = blocks.GetDefinition(
            blocks.GetId("asteria:bloom_cinder"));
        Assert.True(bloom.WindSway);
        Assert.Equal(BlockVisualKind.CrossedSprite, bloom.Visual.Kind);
        var lichen = blocks.GetDefinition(
            blocks.GetId("asteria:lichen_soot"));
        Assert.Equal(BlockVisualKind.GroundSprite, lichen.Visual.Kind);
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
