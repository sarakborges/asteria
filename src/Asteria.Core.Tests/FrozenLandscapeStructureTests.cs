using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FrozenLandscapeStructureTests
{
    [Theory]
    [InlineData("arctic", "frostveil", "frozen_plain", "rocky_ridges")]
    [InlineData("arctic", "rimehollow", "frozen_plain", "rocky_ridges")]
    [InlineData("alps", "hoarfane", "snowfields", "wind_scoured_rock")]
    [InlineData("alps", "glassmorrow", "snowfields", "wind_scoured_rock")]
    public void RetainedFrozenTreesUseExistingWoodAndFavorSnowyHabitats(
        string biome, string species, string preferred, string avoided)
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        var dimension = DimensionRegistry.FromJson(ReadJson("dimensions"))
            .Get(DimensionId.Overworld);
        var rule = Assert.Single(dimension.GeneratedSurfaceStructures,
            r => r.Biome == "asteria:overworld/" + biome &&
                 r.Structure == "asteria:tree_" + species);
        Assert.True(rule.HabitatWeights!.For(preferred) >
                    rule.HabitatWeights.For(avoided));

        var variants = structures.ResolveReference("asteria:tree_" + species);
        Assert.Equal(2, variants.Count);
        Assert.All(variants, variant =>
        {
            Assert.True(variant.Restrictions.RequiresDryGround);
            Assert.Equal(StructureFluidPolicy.Forbid,
                variant.Generation.FluidPolicy);
            Assert.Contains("asteria:snow", variant.Restrictions.GroundBlocks);
            Assert.Contains("asteria:ice", variant.Restrictions.GroundBlocks);
            Assert.Contains(variant.Voxels,
                voxel => voxel.Block == "asteria:log_" + species);
            Assert.Contains(variant.Voxels,
                voxel => voxel.Block == "asteria:leaf_" + species);
        });
    }

    [Theory]
    [InlineData("spire_rime_arctic", 3)]
    [InlineData("serac_alpine", 3)]
    [InlineData("arch_ice_alpine", 2)]
    public void IceLandmarksRemainAvailable(string group, int expected)
    {
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        var variants = structures.ResolveReference("asteria:" + group);
        Assert.Equal(expected, variants.Count);
        Assert.All(variants, variant => Assert.Contains(variant.Voxels,
            voxel => voxel.Block == "asteria:ice"));
    }

    private static IEnumerable<string> ReadJson(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
