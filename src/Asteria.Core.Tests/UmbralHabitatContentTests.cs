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
    [InlineData("umbral_reach", "asteria:grass_block", "asteria:dirt", "asteria:gravel", "asteria:stone")]
    [InlineData("withered_waste", "asteria:dirt", "asteria:gravel", "asteria:stone", "asteria:stone")]
    public void OrganicSurfacePatchesPreserveWorldSpaceDeterminismAndMaterialVariety(
        string biomeName, string primary, string alternateA,
        string alternateB, string alternateC)
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var biome = LoadBiome(biomeName);
        var patch = Assert.IsType<BiomeSurfacePatchDefinition>(
            biome.SurfaceLayers[0].Patch);
        Assert.True(patch.WarpStrength > 0d);
        Assert.True(patch.DetailScale > 0d);

        var a = new BiomeSurfaceMaterialField(771UL, [biome], blocks);
        var b = new BiomeSurfaceMaterialField(771UL, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var seen = new HashSet<BlockRuntimeId>();

        for (var z = -160; z <= 160; z += 4)
        {
            for (var x = -160; x <= 160; x += 4)
            {
                var actual = a.BlockAt(sample, x, z, 0);
                Assert.Equal(actual, b.BlockAt(sample, x, z, 0));
                seen.Add(actual);
            }
        }

        Assert.Contains(blocks.GetId(primary), seen);
        Assert.Contains(blocks.GetId(alternateA), seen);
        Assert.Contains(blocks.GetId(alternateB), seen);
        Assert.Contains(blocks.GetId(alternateC), seen);
    }

    [Fact]
    public void UmbralReachProducesSparseGrovesAndRocksWithoutFillingMeadows()
    {
        var biome = LoadBiome("umbral_reach");
        var roots = LoadUmbralRoots(biome);
        Assert.Equal(5, roots.Length);
        var tree = Assert.Single(roots,
            root => root.Structure == "asteria:tree_wraith");
        var grove = Assert.Single(roots,
            root => root.Structure == "asteria:wraith_grove");
        var rocks = Assert.Single(roots,
            root => root.Structure == "asteria:rock_cluster");

        Assert.True(tree.HabitatWeights!.For("spectral_groves") >
                    tree.HabitatWeights.For("shadow_meadows"));
        Assert.Equal(0f, grove.HabitatWeights!.For("shadow_meadows"));
        Assert.True(rocks.HabitatWeights!.For("stony_fields") >
                    rocks.HabitatWeights.For("spectral_groves"));
        Assert.True(Assert.Single(biome.Decorations,
            decoration => decoration.Block == "asteria:grass")
            .HabitatWeights!.For("shadow_meadows") >
            Assert.Single(biome.Decorations,
                decoration => decoration.Block == "asteria:grass")
                .HabitatWeights!.For("stony_fields"));

        ValidateHabitatRules(biome, roots);
    }

    [Fact]
    public void WitheredWastePreservesDryExpansesAndConcentratesSnagsAndRocks()
    {
        var biome = LoadBiome("withered_waste");
        var roots = LoadUmbralRoots(biome);
        Assert.Equal(6, roots.Length);
        var snag = Assert.Single(roots,
            root => root.Structure == "asteria:withered_snag");
        var deadfall = Assert.Single(roots,
            root => root.Structure == "asteria:fallen_log_wraith");
        var cluster = Assert.Single(roots,
            root => root.Structure == "asteria:rock_cluster");

        Assert.True(snag.HabitatWeights!.For("withered_thickets") >
                    snag.HabitatWeights.For("dead_expanse"));
        Assert.True(deadfall.HabitatWeights!.For("withered_thickets") >
                    deadfall.HabitatWeights.For("rocky_barrens"));
        Assert.True(cluster.HabitatWeights!.For("rocky_barrens") >
                    cluster.HabitatWeights.For("dead_expanse"));
        Assert.DoesNotContain(biome.Decorations,
            decoration => decoration.Block == "asteria:grass");

        ValidateHabitatRules(biome, roots);
    }

    [Fact]
    public void WitheredSnagTemplatesNeverConvertDeadSoilIntoGrass()
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var deadTrees = structures.ResolveReference("asteria:withered_snag");
        Assert.Equal(2, deadTrees.Count);
        foreach (var tree in deadTrees)
        {
            Assert.Equal(0, tree.GroundAnchorYOffset);
            Assert.Equal(1f, tree.Restrictions.RequiredBiomeCoverage);
            Assert.True(tree.Restrictions.RequiresDryGround);
            Assert.Equal(StructureFluidPolicy.Forbid, tree.Generation.FluidPolicy);
            Assert.Equal(StructureReplacePolicy.Terrain, tree.Generation.ReplacePolicy);
            Assert.Contains("asteria:dirt", tree.Restrictions.GroundBlocks);
            Assert.Contains("asteria:gravel", tree.Restrictions.GroundBlocks);
            Assert.All(tree.Voxels, voxel =>
            {
                Assert.True(voxel.Y >= 1);
                Assert.NotEqual("asteria:grass_block", voxel.Block);
            });
        }
        structures.ValidateBlocks(blocks);
    }

    [Fact]
    public void DeadfallTemplatesUsePaleDeadwoodAndStayGroundBound()
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var variants = structures.ResolveReference("asteria:fallen_log_wraith");
        Assert.Equal(2, variants.Count);

        foreach (var structure in variants)
        {
            Assert.False(structure.Locatable);
            Assert.True(structure.Rotation);
            Assert.Equal(0, structure.GroundAnchorYOffset);
            Assert.Equal(0, structure.Restrictions.MaxSlope);
            Assert.Equal(1f, structure.Restrictions.RequiredBiomeCoverage);
            Assert.True(structure.Restrictions.RequiresDryGround);
            Assert.Equal(StructureFluidPolicy.Forbid, structure.Generation.FluidPolicy);
            Assert.Equal(StructureReplacePolicy.Terrain, structure.Generation.ReplacePolicy);
            Assert.False(structure.Generation.ReserveSpace);
            Assert.Contains("wood_debris", structure.ConflictGroups);
            Assert.Contains("asteria:dirt", structure.Restrictions.GroundBlocks);
            Assert.Contains("asteria:gravel", structure.Restrictions.GroundBlocks);
            Assert.All(structure.Voxels, cell =>
            {
                Assert.True(cell.Y >= 1);
                Assert.Contains(cell.Block, new[]
                {
                    "asteria:log_enchanted",
                    "asteria:log_enchanted_stripped",
                    "asteria:log_enchanted_stripped_hollow",
                });
                Assert.Equal(BlockOrientation.X, cell.Orientation);
            });
        }

        structures.ValidateBlocks(blocks);
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
