using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BiomeBiodiversityExpansionTests
{
    [Fact]
    public void PlainsHasThreeFloweringHabitats()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadJson("objects"), ObjectDefinition.Parse, definition => definition.Id);
        biomes.ValidateObjects(objects);
        var plains = biomes.Get("asteria:overworld/plains");
        var names = new[] {
            "wildflower_blue", "wildflower_yellow",
            "daisy"
        };
        Assert.Equal(4, plains.SurfaceHabitats!.Bands.Count);
        Assert.Equal("asteria:block_grass", plains.SurfaceLayers[0].Block);
        foreach (var name in names)
        {
            var rule = Assert.Single(plains.Decorations,
                d => d.Block == $"asteria:{name}");
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.Conditions);
            Assert.NotNull(rule.HabitatWeights);
            Assert.Contains("asteria:block_grass", rule.SurfaceBlocks);
            Assert.True(rule.HabitatWeights!.For("open") >
                rule.HabitatWeights.For("rocky"));
            Assert.True(rule.IsObject);
            Assert.False(blocks.TryGetId(rule.Block, out _));
            var flower = objects.Get(rule.Block);
            Assert.True(flower.Supports(BlockFace.Top));
            Assert.True(flower.WindSway);
            Assert.Equal(ObjectVisualKind.CrossedSprite, flower.Visual.Kind);
        }
        var grass = Assert.Single(plains.Decorations,
            d => d.Block == "asteria:grass");
        Assert.True(grass.HabitatWeights!.For("thicket") >
                    grass.HabitatWeights.For("grove"));
    }

    [Fact]
    public void SwampHasNearWaterCattailsFernsAndGiantMushroomStructures()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        biomes.ValidateBlocks(blocks);
        var swamp = biomes.Get("asteria:overworld/swamp");

        foreach (var name in new[] { "cattail", "fern_marsh" })
        {
            var rule = Assert.Single(swamp.Decorations,
                d => d.Block == $"asteria:{name}");
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotNull(rule.Conditions);
            if (name == "cattail")
            {
                Assert.True(rule.IsObject);
                var objects = PackContentRegistry<ObjectDefinition>.FromJson(
                    ReadJson("objects"), ObjectDefinition.Parse, d => d.Id);
                var cattailObject = objects.Get(rule.Block);
                Assert.True(cattailObject.Supports(BlockFace.Top));
                Assert.Equal(ObjectVisualKind.CrossedSprite, cattailObject.Visual.Kind);
            }
            else
            {
                var block = blocks.GetDefinition(blocks.GetId(rule.Block));
                Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
                Assert.False(block.IsCollidable);
            }
        }
        var cattail = Assert.Single(swamp.Decorations,
            d => d.Block == "asteria:cattail");
        Assert.Equal(DecorationFluidRelation.Nearby,
            cattail.FluidRequirement!.Relation);
        Assert.Equal("asteria:water", cattail.FluidRequirement.Fluid);
        Assert.Equal(2, cattail.FluidRequirement.MaxDistance);
        Assert.True(cattail.HabitatWeights!.For("open_mire") >
                    cattail.HabitatWeights.For("fungal_ground"));

        var variants = structures.ResolveReference(
            "asteria:mushroom_swamp_giant");
        Assert.Equal(3, variants.Count);
        Assert.All(variants, variant =>
        {
            Assert.True(variant.MaximumY >= 5);
            Assert.True(variant.Voxels.Count >= 16);
            Assert.True(variant.Restrictions.RequiresDryGround);
            Assert.Contains(variant.Voxels,
                v => v.Block == "asteria:mushroom_stem_marsh_giant");
            Assert.Contains(variant.Voxels,
                v => v.Block == "asteria:mushroom_cap_marsh_giant");
            Assert.True(variant.Voxels.Count(v =>
                v.Block == "asteria:mushroom_cap_marsh_giant") >= 9);
        });
        var ruleRoot = Assert.Single(
            DimensionRegistry.FromJson(ReadJson("dimensions"))
                .Get(DimensionId.Overworld).GeneratedSurfaceStructures,
            r => r.Biome == swamp.Id &&
                 r.Structure == "asteria:mushroom_swamp_giant");
        Assert.True(ruleRoot.HabitatWeights!.For("fungal_ground") >
                    ruleRoot.HabitatWeights.For("willow_grove"));
        Assert.True(ruleRoot.HabitatWeights.For("open_mire") <
                    ruleRoot.HabitatWeights.For("willow_grove"));
    }

    [Fact]
    public void SwampCattailRequiresNearbyWaterAndSelectionIsSeedDeterministic()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var swamp = BiomeRegistry.FromJson(ReadJson("biomes"))
            .Get("asteria:overworld/swamp");
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadJson("objects"), ObjectDefinition.Parse, d => d.Id);
        var a = new SurfaceDecorationField(542, [swamp], blocks,
            fluids: fluids, objects: objects);
        var b = new SurfaceDecorationField(542, [swamp], blocks,
            fluids: fluids, objects: objects);
        var mud = blocks.GetId("asteria:mud");
        const string cattail = "asteria:cattail";
        var sample = new BiomeSample(swamp.Id,
            [new BiomeInfluence(swamp.Id, 1f)]);
        var context = new SurfacePlacementContext(90, 0);
        var found = false;
        for (var z = -48; z <= 48; z++)
        for (var x = -48; x <= 48; x++)
        {
            var dry = a.ObjectAt(sample, mud, x, z, context,
                supportY: 89, nearbyFluid: (_, _, _, _, _) => false);
            var wet = a.ObjectAt(sample, mud, x, z, context,
                supportY: 89, nearbyFluid: (_, _, _, _, _) => true);
            Assert.Equal(wet, b.ObjectAt(sample, mud, x, z, context,
                supportY: 89, nearbyFluid: (_, _, _, _, _) => true));
            // Other dry Objects (e.g. sticks) may still appear here.
            Assert.NotEqual(cattail, dry);
            found |= wet == cattail;
        }
        Assert.True(found, "Expected nearby-water cattail patches.");
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
