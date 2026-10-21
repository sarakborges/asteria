using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class UmbralHabitatContentTests
{
    [Theory]
    [InlineData("umbral_reach", "shadow_meadows", "spectral_groves", "stony_fields")]
    [InlineData("withered_waste", "dead_expanse", "withered_thickets", "rocky_barrens")]
    public void UmbralBiomesAuthorDistinctDeterministicHabitats(
        string biomeName, string open, string woody, string rocky)
    {
        var biome = LoadBiome(biomeName);
        var bands = Assert.IsType<SurfaceHabitatDefinition>(biome.SurfaceHabitats);
        Assert.Equal(new[] { open, woody, rocky }, bands.Bands.Select(band => band.Id));
        Assert.InRange(bands.TransitionWidth, 0.05f, 0.2f);

        var a = new SurfaceHabitatField(0xA57EUL, [biome]);
        var b = new SurfaceHabitatField(0xA57EUL, [biome]);
        foreach (var (x, z) in new[] { (-207, -131), (0, 0), (89, 127), (313, -196) })
            Assert.Equal(a.Sample(biome.Id, x, z),
                         b.Sample(biome.Id, x, z));

        Assert.NotEmpty(biome.Decorations);
        foreach (var decoration in biome.Decorations)
            bands.ValidateWeights(Assert.IsType<SurfaceHabitatWeights>(
                decoration.HabitatWeights));
    }

    [Theory]
    [InlineData("umbral_reach", "asteria:block_grass", "asteria:dirt", "asteria:gravel")]
    [InlineData("withered_waste", "asteria:dirt", "asteria:gravel", "asteria:cobble_stone")]
    public void OrganicGeologyPreservesWorldSpaceDeterminismAndMaterialVariety(
        string biomeName, string surfaceBlock, string alternateA, string alternateB)
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var biome = LoadBiome(biomeName);
        var geology = Assert.IsType<BiomeGeologyDefinition>(biome.Geology);
        Assert.Null(biome.SurfaceLayers[0].Patch);
        Assert.True(geology.SurfaceCoverDepth > 0);

        var a = new BiomeSurfaceMaterialField(771UL, [biome], blocks);
        var b = new BiomeSurfaceMaterialField(771UL, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var seen = new HashSet<BlockRuntimeId>();

        for (var z = -384; z <= 384; z += 8)
        for (var x = -384; x <= 384; x += 8)
        {
            Assert.Equal(blocks.GetId(surfaceBlock), a.BlockAt(sample, x, z, 0));
            Assert.True(a.TryGeologyBlockAt(sample, x, 40, z, out var actual));
            Assert.True(b.TryGeologyBlockAt(sample, x, 40, z, out var repeated));
            Assert.Equal(actual, repeated);
            seen.Add(actual);
        }

        Assert.Contains(blocks.GetId(geology.BaseBlock), seen);
        Assert.Contains(blocks.GetId(alternateA), seen);
        Assert.Contains(blocks.GetId(alternateB), seen);
    }

    [Fact]
    public void UmbralReachKeepsSparseShadowTreesWithinSpectralGroves()
    {
        var biome = LoadBiome("umbral_reach");
        var roots = LoadUmbralRoots(biome);
        Assert.Equal(5, roots.Length);
        foreach (var species in new[] { "gloamroot", "nighthollow" })
        {
            var tree = Assert.Single(roots,
                r => r.Structure == "asteria:tree_" + species);
            Assert.True(tree.HabitatWeights!.For("spectral_groves") >
                        tree.HabitatWeights.For("shadow_meadows"));
        }
        var rocks = Assert.Single(roots,
            root => root.Structure == "asteria:cluster_rock");
        Assert.True(rocks.HabitatWeights!.For("stony_fields") >
                    rocks.HabitatWeights.For("spectral_groves"));
        ValidateHabitatRules(biome, roots);
    }

    [Fact]
    public void WitheredWasteConcentratesMourningbriarInWitheredThickets()
    {
        var biome = LoadBiome("withered_waste");
        var roots = LoadUmbralRoots(biome);
        Assert.Equal(5, roots.Length);
        var tree = Assert.Single(roots,
            root => root.Structure == "asteria:tree_mourningbriar");
        var rock = Assert.Single(roots,
            root => root.Structure == "asteria:cluster_rock");
        Assert.True(tree.HabitatWeights!.For("withered_thickets") >
                    tree.HabitatWeights.For("dead_expanse"));
        Assert.True(rock.HabitatWeights!.For("rocky_barrens") >
                    rock.HabitatWeights.For("dead_expanse"));
        Assert.DoesNotContain(biome.Decorations,
            decoration => decoration.Block == "asteria:grass");
        ValidateHabitatRules(biome, roots);
    }

    [Fact]
    public void EveryRetainedUmbralTreeUsesAvailableWoodAndFoliage()
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        structures.ValidateBlocks(blocks);
        foreach (var species in new[] { "gloamroot", "nighthollow", "mourningbriar" })
        {
            var members = structures.ResolveReference("asteria:tree_" + species);
            Assert.Equal(2, members.Count);
            Assert.All(members, tree =>
            {
                Assert.True(tree.Restrictions.RequiresDryGround);
                Assert.Contains(tree.Voxels, v => v.Block == "asteria:log_" + species);
                Assert.Contains(tree.Voxels, v => v.Block == "asteria:leaf_" + species);
            });
        }
    }

    private static void ValidateHabitatRules(
        BiomeDefinition biome,
        IReadOnlyList<DimensionGeneratedSurfaceStructureDefinition> rules)
    {
        var bands = Assert.IsType<SurfaceHabitatDefinition>(biome.SurfaceHabitats);
        foreach (var rule in rules)
        {
            Assert.Equal(biome.Id, rule.Biome);
            bands.ValidateWeights(Assert.IsType<SurfaceHabitatWeights>(rule.HabitatWeights));
            Assert.True(rule.Jitter < rule.Spacing / 2);
        }
    }

    private static DimensionGeneratedSurfaceStructureDefinition[] LoadUmbralRoots(
        BiomeDefinition biome)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", "dimensions", "umbral.json");
        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(path));
        return dimension.GeneratedSurfaceStructures
            .Where(root => root.Biome == biome.Id).ToArray();
    }

    private static BiomeDefinition LoadBiome(string fileName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", "biomes", fileName + ".json");
        return BiomeDefinitionJson.Parse(File.ReadAllText(path));
    }

    private static IEnumerable<string> ReadJsonDirectory(string category)
    {
        var directory = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", category);
        return Directory.EnumerateFiles(directory, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
