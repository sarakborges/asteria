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
    public void CrossedSpriteVegetationRetainsAuthoredDimensionsSupportAndWind()
    {
        var blocks = BlockRegistry.FromJson(PackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            PackJson("objects"), ObjectDefinition.Parse, definition => definition.Id);
        foreach (var (name, width, height, windSway) in new[]
        {
            ("grass_tall", .88f, .94f, true),
            ("orchid_marsh", .77f, .86f, true),
            ("bulrush", .79f, .93f, true),
            ("cattail", .74f, .94f, false)
        })
        {
            var id = "asteria:" + name;
            Assert.False(blocks.TryGetId(id, out _));
            var definition = objects.Get(id);
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
    public void SurfaceHabitatAndNearbyWaterRulesRemainAuthoritative()
    {
        var blocks = BlockRegistry.FromJson(PackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(PackJson("biomes"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            PackJson("objects"), ObjectDefinition.Parse, d => d.Id);
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);

        var grass = Assert.Single(
            biomes.Get("asteria:overworld/plains").Decorations,
            d => d.Block == "asteria:grass_tall");
        Assert.True(grass.IsObject);
        Assert.Equal(DecorationFluidPlacement.Dry, grass.FluidPlacement);
        Assert.True(grass.HabitatWeights!.For("thicket") >
                    grass.HabitatWeights.For("grove"));

        var swamp = biomes.Get("asteria:overworld/swamp");
        foreach (var (name, radius) in new[]
        {
            ("orchid_marsh", 3),
            ("bulrush", 2),
            ("cattail", 2)
        })
        {
            var rule = Assert.Single(swamp.Decorations,
                d => d.Block == "asteria:" + name);
            Assert.True(rule.IsObject);
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
            Assert.Equal("asteria:water", rule.FluidRequirement!.Fluid);
            Assert.Equal(DecorationFluidRelation.Nearby,
                rule.FluidRequirement.Relation);
            Assert.Equal(radius, rule.FluidRequirement.MaxDistance);
            Assert.NotEmpty(rule.SurfaceBlocks);
        }
    }

    [Fact]
    public void ReedBedStructuresArePureObjectPalettesWithSupportFaces()
    {
        var blocks = BlockRegistry.FromJson(PackJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            PackJson("objects"), ObjectDefinition.Parse, d => d.Id);
        var structures = StructureRegistry.FromJson(PackJson("structures"));
        structures.ValidateBlocks(blocks);
        structures.ValidateObjects(objects);
        var variants = structures.ResolveReference("asteria:bed_reed_swamp");
        Assert.Equal(3, variants.Count);
        foreach (var variant in variants)
        {
            Assert.Empty(variant.Voxels);
            Assert.True(variant.Objects.Count >= 8);
            Assert.All(variant.Objects, obj =>
            {
                Assert.Equal(1, obj.Y);
                Assert.Equal(BlockFace.Top, obj.Face);
                Assert.Contains(obj.Object,
                    new[] { "asteria:bulrush", "asteria:cattail" });
            });
            Assert.Equal(StructureFluidPolicy.Forbid,
                variant.Generation.FluidPolicy);
            Assert.True(variant.Restrictions.RequiresDryGround);
        }
    }
}
