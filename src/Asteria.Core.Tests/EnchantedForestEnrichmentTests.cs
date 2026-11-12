using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class EnchantedForestEnrichmentTests
{
    [Fact]
    public void RetainedForestFlowersAndMushroomsUseTheirAuthoredHabitats()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, obj => obj.Id);
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);
        var biome = biomes.Get("asteria:overworld/enchanted_forest");

        Assert.Equal(8, biome.Decorations.Count);
        Assert.Equal("asteria:block_grass", biome.SurfaceLayers[0].Block);
        Assert.Equal(["luminous_clearing", "violet_undergrowth", "pink_glade"],
            biome.SurfaceHabitats!.Bands.Select(x => x.Id).ToArray());

        foreach (var name in new[] { "starflower", "bloom_pink" })
        {
            var rule = Assert.Single(biome.Decorations,
                item => item.Block == "asteria:" + name);
            Assert.False(rule.IsObject);
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.Conditions);
            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            Assert.Equal(BlockVisualKind.CrossedSprite, block.Visual.Kind);
            Assert.False(block.IsCollidable);
            Assert.Equal("textures/objects/" + name + ".png",
                block.Visual.Texture!.Value.Texture);
        }

        foreach (var name in new[]
        {
            "mushroom_purple", "mushroom_pink", "mushroom_blue", "mushroom_red",
        })
        {
            var rule = Assert.Single(biome.Decorations,
                item => item.Block == "asteria:" + name);
            Assert.True(rule.IsObject);
            Assert.NotNull(rule.HabitatWeights);
            var mushroom = objects.Get(rule.Block);
            Assert.Equal(ObjectVisualKind.CrossedSprite, mushroom.Visual.Kind);
            Assert.True(mushroom.Supports(BlockFace.Top));
        }
    }

    [Fact]
    public void MushroomRingsHaveEmptyCentersAndNoGroundOverwrite()
    {
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        var variants = structures.ResolveReference("asteria:ring_mushroom_enchanted");
        Assert.Equal(3, variants.Count);
        Assert.All(variants, ring => {
            Assert.InRange(ring.Voxels.Count + ring.Objects.Count, 8, 32);
            Assert.All(ring.Voxels, x => Assert.Equal(1, x.Y));
            Assert.All(ring.Objects, x => Assert.Equal(1, x.Y));
            Assert.DoesNotContain(ring.Voxels, x => x.X == 0 && x.Z == 0);
            Assert.DoesNotContain(ring.Objects, x => x.X == 0 && x.Z == 0);
            Assert.Empty(ring.Voxels);
            Assert.All(ring.Objects, obj => {
                Assert.Contains(obj.Object, new[] {
                    "asteria:mushroom_red", "asteria:mushroom_blue",
                    "asteria:mushroom_pink", "asteria:mushroom_purple"
                });
                Assert.Equal(BlockFace.Top, obj.Face);
            });
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
            ("ring_mushroom_enchanted",3),
            ("canopy_fungal_enchanted",2)
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
                    block, new[] {"asteria:block_grass","asteria:dirt"}));
                Assert.All(s.Voxels, v => Assert.True(v.Y > 0));
                Assert.Equal(StructureFluidPolicy.Forbid, s.Generation.FluidPolicy);
            });
        }
        var canopy = structures.ResolveReference("asteria:canopy_fungal_enchanted");
        Assert.True(canopy.Max(s => s.MaximumY) >= 8);
        Assert.All(canopy, s => {
            Assert.Contains(s.Voxels, v =>
                v.Block == "asteria:fungus_stem_luminous_giant");
            Assert.Contains(s.Voxels, v =>
                v.Block == "asteria:fungus_cap_luminous_giant");
        });
        var cap = blocks.GetDefinition(
            blocks.GetId("asteria:fungus_cap_luminous_giant"));
        Assert.Equal(BlockTint.None, cap.Tint);
        Assert.True(cap.LightEmission.Red > 0);
        Assert.True(cap.LightEmission.Blue > 0);

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
