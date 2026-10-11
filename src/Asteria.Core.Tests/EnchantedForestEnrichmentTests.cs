using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class EnchantedForestEnrichmentTests
{
    [Fact]
    public void FlowersAreHabitatSpecificAndMushroomsAreReused()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var biome = biomes.Get("asteria:overworld/enchanted_forest");
        Assert.Equal(10, biome.Decorations.Count);
        Assert.Equal("asteria:grass_block", biome.SurfaceLayers[0].Block);
        Assert.Equal(["luminous_clearing","violet_undergrowth","pink_glade"],
            biome.SurfaceHabitats!.Bands.Select(x => x.Id).ToArray());

        foreach (var name in new[] {
            "enchanted_starflower","enchanted_violet_fern",
            "enchanted_pink_bloom","enchanted_glow_pod"
        })
        {
            var rule = Assert.Single(biome.Decorations,
                x => x.Block == "asteria:" + name);
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotNull(rule.Cluster);
            Assert.All(rule.SurfaceBlocks, x =>
                Assert.Contains(x, new[] {"asteria:grass_block","asteria:dirt"}));
            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            Assert.Equal(BlockTint.None, block.Tint);
            Assert.Equal(BlockVisualKind.CrossedSprite, block.Visual.Kind);
            Assert.True(block.WindSway);
            Assert.False(block.IsCollidable);
            Assert.Equal("textures/objects/" + name + ".png",
                block.Visual.Texture!.Value.Texture);
        }
        foreach (var reused in new[] {"mushroom_red","mushroom_blue"})
            Assert.Single(biome.Decorations,
                x => x.Block == "asteria:" + reused);

        foreach (var glow in new[] {
            "enchanted_starflower","enchanted_glow_pod"
        })
            Assert.True(blocks.GetDefinition(
                blocks.GetId("asteria:" + glow)).LightEmission.Blue > 0);
    }

    [Fact]
    public void MushroomRingsHaveEmptyCentersAndNoGroundOverwrite()
    {
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        var variants = structures.ResolveReference("asteria:enchanted_mushroom_ring");
        Assert.Equal(3, variants.Count);
        Assert.All(variants, ring => {
            Assert.InRange(ring.Voxels.Count, 8, 32);
            Assert.All(ring.Voxels, x => Assert.Equal(1, x.Y));
            Assert.DoesNotContain(ring.Voxels, x => x.X == 0 && x.Z == 0);
            Assert.All(ring.Voxels, x => Assert.Contains(x.Block, new[] {
                "asteria:mushroom_red","asteria:mushroom_blue",
                "asteria:mushroom_purple","asteria:mushroom_pink"
            }));
        });
    }

    [Fact]
    public void CanopiesAndStumpsAreGroundedAndReuseEnchantedMaterials()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        structures.ValidateBlocks(blocks);
        var dimension = DimensionRegistry.FromJson(ReadPackJson("dimensions"))
            .Get(DimensionId.Overworld);
        foreach (var (name, count) in new[] {
            ("enchanted_mushroom_ring",3),
            ("enchanted_hollow_stump",2),
            ("enchanted_fungal_canopy",3)
        })
        {
            Assert.Single(dimension.GeneratedSurfaceStructures, x =>
                x.Biome == "asteria:overworld/enchanted_forest" &&
                x.Structure == "asteria:" + name);
            var variants = structures.ResolveReference("asteria:" + name);
            Assert.Equal(count, variants.Count);
            Assert.All(variants, s => {
                Assert.Equal((int?)0, s.GroundAnchorY);
                Assert.True(s.Restrictions.RequiresDryGround);
                Assert.Equal(1f, s.Restrictions.RequiredBiomeCoverage);
                Assert.All(s.Restrictions.GroundBlocks, block => Assert.Contains(
                    block, new[] {"asteria:grass_block","asteria:dirt"}));
                Assert.All(s.Voxels, v => Assert.True(v.Y > 0));
                Assert.Equal(StructureFluidPolicy.Forbid, s.Generation.FluidPolicy);
            });
        }
        var canopy = structures.ResolveReference("asteria:enchanted_fungal_canopy");
        Assert.True(canopy.Max(s => s.MaximumY) >= 10);
        Assert.All(canopy, s => {
            Assert.Contains(s.Voxels, v =>
                v.Block == "asteria:enchanted_giant_fungus_stem");
            Assert.Contains(s.Voxels, v =>
                v.Block == "asteria:enchanted_giant_fungus_cap");
        });
        var cap = blocks.GetDefinition(
            blocks.GetId("asteria:enchanted_giant_fungus_cap"));
        Assert.Equal(BlockTint.None, cap.Tint);
        Assert.True(cap.LightEmission.Red > 0);
        Assert.True(cap.LightEmission.Blue > 0);

        Assert.All(structures.ResolveReference("asteria:enchanted_hollow_stump"),
            s => {
                Assert.Contains(s.Voxels, v => v.Block == "asteria:log_enchanted");
                Assert.Contains(s.Voxels, v => v.Block == "asteria:enchanted_hollow_wood");
            });
    }

    private static IEnumerable<string> ReadPackJson(string folder)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", folder);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
