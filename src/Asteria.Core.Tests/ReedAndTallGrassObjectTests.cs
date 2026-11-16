using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ReedAndTallGrassObjectTests
{
    private static IEnumerable<string> PackJson(string category) =>
        Directory.EnumerateFiles(Path.Combine(AppContext.BaseDirectory,
                "packs", "default", "data", category), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);

    [Fact]
    public void RetainedGrassAndCattailKeepTheirAuthoredObjectGeometry()
    {
        var blocks = BlockRegistry.FromJson(PackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            PackJson("objects"), ObjectDefinition.Parse, definition => definition.Id);
        foreach (var (name, width, height, windSway) in new[]
        {
            ("grass", .72f, .62f, true),
            ("cattail", .74f, .94f, false)
        })
        {
            Assert.False(blocks.TryGetId("asteria:" + name, out _));
            var definition = objects.Get("asteria:" + name);
            Assert.Equal(ObjectVisualKind.CrossedSprite, definition.Visual.Kind);
            Assert.Equal("textures/objects/" + name + ".png",
                definition.Visual.Texture);
            Assert.Equal(width, definition.Visual.Width);
            Assert.Equal(height, definition.Visual.Height);
            Assert.Equal(4, definition.Visual.Planes);
            Assert.Equal(.5f, definition.Visual.AlphaCutoff);
            Assert.Equal(windSway, definition.WindSway);
            Assert.False(definition.DropSelf);
            Assert.True(definition.Supports(BlockFace.Top));
        }
    }

    [Fact]
    public void RetainedSurfacePlantsKeepTheirBiomeAndWaterRules()
    {
        var blocks = BlockRegistry.FromJson(PackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(PackJson("biomes"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            PackJson("objects"), ObjectDefinition.Parse, definition => definition.Id);
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);

        var plains = biomes.Get("asteria:overworld/plains");
        var grass = Assert.Single(plains.Decorations,
            decoration => decoration.Block == "asteria:grass");
        Assert.True(grass.IsObject);
        Assert.Equal(DecorationFluidPlacement.Dry, grass.FluidPlacement);
        Assert.True(grass.HabitatWeights!.For("open") > grass.HabitatWeights.For("rocky"));

        var swamp = biomes.Get("asteria:overworld/swamp");
        var cattail = Assert.Single(swamp.Decorations,
            decoration => decoration.Block == "asteria:cattail");
        Assert.Equal(DecorationFluidPlacement.Dry, cattail.FluidPlacement);
        Assert.Equal("asteria:water", cattail.FluidRequirement!.Fluid);
        Assert.Equal(DecorationFluidRelation.Nearby, cattail.FluidRequirement.Relation);
        Assert.Equal(2, cattail.FluidRequirement.MaxDistance);

        var lily = Assert.Single(swamp.Decorations,
            decoration => decoration.Block == "asteria:pad_lily");
        Assert.Equal("asteria:water", lily.FluidRequirement!.Fluid);
        Assert.Equal(DecorationFluidRelation.Below, lily.FluidRequirement.Relation);
        Assert.Equal(0, lily.FluidRequirement.MaxDistance);
        Assert.True(objects.Get(lily.Block).Supports(BlockFace.Top));
    }

    [Fact]
    public void RemovedReedBedsAndMossStumpsHaveNoSpawnRulesOrStructureFiles()
    {
        var structures = StructureRegistry.FromJson(PackJson("structures"));
        var dimension = DimensionRegistry.FromJson(PackJson("dimensions"))
            .Get(DimensionId.Overworld);
        Assert.DoesNotContain(dimension.GeneratedSurfaceStructures,
            rule => rule.Structure is "asteria:bed_reed_swamp" or
                "asteria:stump_swamp_moss");
        Assert.Throws<KeyNotFoundException>(() =>
            structures.ResolveReference("asteria:bed_reed_swamp"));
        Assert.Throws<KeyNotFoundException>(() =>
            structures.ResolveReference("asteria:stump_swamp_moss"));
    }
}
