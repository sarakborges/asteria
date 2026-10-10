using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class OceanLandmarkContentTests
{
    private static readonly (string Group, int Count)[] Families =
    [
        ("coral_fan_garden", 3),
        ("coral_table_garden", 3),
        ("coral_pillar_garden", 3),
        ("ocean_kelp_grove", 4),
        ("coral_arch", 2),
    ];

    [Fact]
    public void OceanLandmarksUseAuthoredWaterAcrossTheirEntireGeometry()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        structures.ValidateFluids(fluids);

        foreach (var (group, expected) in Families)
        {
            var variants = structures.ResolveReference("asteria:" + group);
            Assert.Equal(expected, variants.Count);
            Assert.True(variants.Select(s => s.MaximumY).Distinct().Count() > 1);
            foreach (var member in variants)
            {
                Assert.Equal("asteria:water", member.Restrictions.RequiredFluid);
                Assert.False(member.Restrictions.RequiresDryGround);
                Assert.Equal((int?)0, member.GroundAnchorY);
                Assert.Equal(1f, member.Restrictions.RequiredBiomeCoverage);
                Assert.Equal(1, member.Restrictions.MaxSlope);
                Assert.Equal(StructureFluidPolicy.Displace,
                    member.Generation.FluidPolicy);
                Assert.Equal(StructureReplacePolicy.Terrain,
                    member.Generation.ReplacePolicy);
                Assert.InRange(member.MaximumY, 4, 12);
                Assert.True(member.Voxels.Count > 8);
                Assert.All(member.Voxels, voxel =>
                    Assert.True(voxel.Y > 0, "Must preserve natural seafloor."));
                Assert.All(member.Restrictions.GroundBlocks, ground =>
                    Assert.Contains(ground, new[]
                    {
                        "asteria:sand", "asteria:gravel", "asteria:clay",
                        "asteria:stone_cobble"
                    }));
                Assert.Contains(member.Restrictions.Proximity, rule =>
                    rule.Target.Fluid == "asteria:water" &&
                    rule.Mode == StructureProximityMode.Required);
            }
        }
    }

    [Fact]
    public void OceanHasMoreThanSmallReefsAndGroupsAreHabitatWeighted()
    {
        var dimension = DimensionRegistry.FromJson(ReadJson("dimensions"))
            .Get(DimensionId.Overworld);
        var ocean = BiomeRegistry.FromJson(ReadJson("biomes"))
            .Get("asteria:overworld/ocean");
        Assert.Equal(3, ocean.SurfaceHabitats!.Bands.Count);
        Assert.Equal("asteria:sand", ocean.SurfaceLayers[0].Block);
        var authored = dimension.GeneratedSurfaceStructures.Where(rule =>
            rule.Biome == ocean.Id).ToArray();
        foreach (var (group, _) in Families)
        {
            var rule = Assert.Single(authored,
                x => x.Structure == "asteria:" + group);
            Assert.NotNull(rule.HabitatWeights);
            Assert.InRange(rule.Spacing, 30, 130);
            Assert.InRange(rule.Chance, .5f, 1f);
        }
        var fans = Assert.Single(authored, rule =>
            rule.Structure == "asteria:coral_fan_garden");
        Assert.True(fans.HabitatWeights!.For("rocky_reefs") >
                    fans.HabitatWeights.For("sand_flats"));
        var kelp = Assert.Single(authored, rule =>
            rule.Structure == "asteria:ocean_kelp_grove");
        Assert.True(kelp.HabitatWeights!.For("gravel_banks") >
                    kelp.HabitatWeights.For("sand_flats"));
        var arches = Assert.Single(authored, rule =>
            rule.Structure == "asteria:coral_arch");
        Assert.Equal(0f, arches.HabitatWeights!.For("sand_flats"));
    }

    [Fact]
    public void NewCoralsUseDistinctPackTexturesAndKelpHasCutoutFronds()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var texturePaths = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in new[]
        {
            "coral_violet","coral_orange","coral_teal","coral_lime",
            "ocean_kelp_stem","ocean_kelp_frond"
        })
        {
            var block = blocks.GetDefinition(blocks.GetId("asteria:" + name));
            var texture = Assert.Single(block.Textures.ForFace(BlockFace.Top));
            Assert.True(texturePaths.Add(texture.Texture));
            Assert.Equal("textures/blocks/" + name + ".png", texture.Texture);
        }
        Assert.Equal(6, texturePaths.Count);
        var frond = blocks.GetDefinition(blocks.GetId("asteria:ocean_kelp_frond"));
        Assert.True(frond.WindSway);
        Assert.Equal(BlockRenderMode.Cutout, frond.RenderMode);
    }

    [Fact]
    public void RequiredFluidValidationRejectsDryGroundAndUnknownFluid()
    {
        const string template = """
        {
          "id":"asteria:test_submerged",
          "groundAnchorY":0,
          "anchor":{"x":0,"y":0,"z":0},
          "restrictions":{
            "groundBlocks":["asteria:sand"],
            "requiresDryGround":false,
            "requiredFluid":"asteria:water"
          },
          "generation":{"replacePolicy":"terrain","fluidPolicy":"displace"},
          "palette":{"T":{"block":"asteria:stone"}},
          "layers":[{"y":1,"rows":["T"]}]
        }
        """;
        var parsed = StructureDefinitionJson.Parse(template);
        Assert.Equal("asteria:water", parsed.Restrictions.RequiredFluid);
        Assert.Throws<ArgumentException>(() =>
            new StructureRestrictionsDefinition(
                requiresDryGround:true,requiredFluid:"asteria:water"));
    }

    private static IEnumerable<string> ReadJson(string directory)
    {
        var folder = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(folder, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
