using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FrozenLandscapeStructureTests
{
    [Fact]
    public void FrozenBiomesDoNotReferenceRemovedFantasyTrees()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        var dimension = DimensionRegistry.FromJson(ReadJson("dimensions"))
            .Get(DimensionId.Overworld);

        foreach (var biome in new[]
        {
            "asteria:overworld/arctic",
            "asteria:overworld/alps"
        })
        {
            var roots = dimension.GeneratedSurfaceStructures
                .Where(rule => rule.Biome == biome).ToArray();
            Assert.NotEmpty(roots);
            Assert.DoesNotContain(roots,
                root => root.Structure.StartsWith("asteria:tree_", StringComparison.Ordinal));
            foreach (var root in roots)
                Assert.NotEmpty(structures.ResolveReference(root.Structure));
        }

        foreach (var species in new[]
        {
            "frostveil", "rimehollow", "hoarfane", "glassmorrow"
        })
        {
            Assert.False(blocks.TryGetId("asteria:log_" + species, out _));
            Assert.False(blocks.TryGetId("asteria:leaf_" + species, out _));
            Assert.Throws<KeyNotFoundException>(() =>
                structures.ResolveReference("asteria:tree_" + species));
        }
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
