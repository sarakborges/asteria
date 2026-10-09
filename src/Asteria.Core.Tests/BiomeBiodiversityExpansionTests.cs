using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BiomeBiodiversityExpansionTests
{
    [Fact]
    public void PlainsHasThreeFloweringAndTallGrassHabitats()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var plains = biomes.Get("asteria:overworld/plains");
        var names = new[] {
            "prairie_wildflower_blue", "prairie_wildflower_yellow",
            "prairie_tall_grass"
        };
        Assert.Equal(4, plains.SurfaceHabitats!.Bands.Count);
        Assert.Equal("asteria:grass_block", plains.SurfaceLayers[0].Block);
        foreach (var name in names)
        {
            var rule = Assert.Single(plains.Decorations,
                d => d.Block == $"asteria:{name}");
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.Conditions);
            Assert.NotNull(rule.HabitatWeights);
            Assert.Contains("asteria:grass_block", rule.SurfaceBlocks);
            Assert.True(rule.HabitatWeights!.For("open") >
                rule.HabitatWeights.For("rocky"));
            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
            Assert.True(block.WindSway);
            Assert.Equal(BlockVisualKind.CrossedSprite, block.Visual.Kind);
        }
        var grass = Assert.Single(plains.Decorations,
            d => d.Block == "asteria:prairie_tall_grass");
        Assert.True(grass.HabitatWeights!.For("thicket") >
                    grass.HabitatWeights.For("grove"));
    }

    [Fact]
    public void SwampHasNearWaterIrisesFernsAndGiantMushroomStructures()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        biomes.ValidateBlocks(blocks);
        var swamp = biomes.Get("asteria:overworld/swamp");

        foreach (var name in new[] { "swamp_iris", "swamp_fern" })
        {
            var rule = Assert.Single(swamp.Decorations,
                d => d.Block == $"asteria:{name}");
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotNull(rule.Conditions);
            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
            Assert.False(block.IsCollidable);
        }
        var iris = Assert.Single(swamp.Decorations,
            d => d.Block == "asteria:swamp_iris");
        Assert.Equal(DecorationFluidRelation.Nearby,
            iris.FluidRequirement!.Relation);
        Assert.Equal("asteria:water", iris.FluidRequirement.Fluid);
        Assert.Equal(3, iris.FluidRequirement.MaxDistance);
        Assert.True(iris.HabitatWeights!.For("open_mire") >
                    iris.HabitatWeights.For("fungal_ground"));

        var variants = structures.ResolveReference(
            "asteria:swamp_giant_mushroom");
        Assert.Equal(3, variants.Count);
        Assert.All(variants, variant =>
        {
            Assert.True(variant.MaximumY >= 5);
            Assert.True(variant.Voxels.Count >= 16);
            Assert.True(variant.Restrictions.RequiresDryGround);
            Assert.Contains(variant.Voxels,
                v => v.Block == "asteria:swamp_giant_mushroom_stem");
            Assert.Contains(variant.Voxels,
                v => v.Block == "asteria:swamp_giant_mushroom_cap");
            Assert.True(variant.Voxels.Count(v =>
                v.Block == "asteria:swamp_giant_mushroom_cap") >= 9);
        });
        var ruleRoot = Assert.Single(
            DimensionRegistry.FromJson(ReadJson("dimensions"))
                .Get(DimensionId.Overworld).GeneratedSurfaceStructures,
            r => r.Biome == swamp.Id &&
                 r.Structure == "asteria:swamp_giant_mushroom");
        Assert.True(ruleRoot.HabitatWeights!.For("fungal_ground") >
                    ruleRoot.HabitatWeights.For("willow_grove"));
        Assert.True(ruleRoot.HabitatWeights.For("open_mire") <
                    ruleRoot.HabitatWeights.For("willow_grove"));
    }

    [Fact]
    public void SwampIrisRequiresNearbyWaterAndSelectionIsSeedDeterministic()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var swamp = BiomeRegistry.FromJson(ReadJson("biomes"))
            .Get("asteria:overworld/swamp");
        var a = new SurfaceDecorationField(542, [swamp], blocks,
            fluids: fluids);
        var b = new SurfaceDecorationField(542, [swamp], blocks,
            fluids: fluids);
        var mud = blocks.GetId("asteria:mud");
        var iris = blocks.GetId("asteria:swamp_iris");
        var sample = new BiomeSample(swamp.Id,
            [new BiomeInfluence(swamp.Id, 1f)]);
        var context = new SurfacePlacementContext(90, 0);
        var found = false;
        for (var z = -48; z <= 48; z++)
        for (var x = -48; x <= 48; x++)
        {
            var dry = a.BlockAt(sample, mud, x, z, context,
                supportY: 89, nearbyFluid: (_, _, _, _, _) => false);
            var wet = a.BlockAt(sample, mud, x, z, context,
                supportY: 89, nearbyFluid: (_, _, _, _, _) => true);
            Assert.Equal(wet, b.BlockAt(sample, mud, x, z, context,
                supportY: 89, nearbyFluid: (_, _, _, _, _) => true));
            Assert.NotEqual(iris, dry);
            found |= wet == iris;
        }
        Assert.True(found, "Expected nearby-water iris patches.");
    }

    private static IEnumerable<string> ReadJson(string dir)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", dir);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
