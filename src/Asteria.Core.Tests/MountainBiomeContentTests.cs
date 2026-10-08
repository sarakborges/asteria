using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class MountainBiomeContentTests
{
    private static readonly string PackData =
        Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");

    [Theory]
    [InlineData("mountains", "foothill_shelves", "talus_fields", "exposed_ridges", 7)]
    [InlineData("mountain_belt", "sheltered_passes", "boulder_runs", "bare_crests", 7)]
    [InlineData("alps", "snowfields", "glacial_ice", "wind_scoured_rock", 6)]
    [InlineData("gorge", "valley_floor", "rubble_slopes", "high_rims", 5)]
    public void MountainHabitatsAreFullyDataDrivenAndSharedAcrossDecorationsAndStructures(
        string file, string firstBand, string middleBand, string lastBand, int rootCount)
    {
        var biome = LoadBiome(file);
        var definition = Assert.IsType<SurfaceHabitatDefinition>(biome.SurfaceHabitats);
        Assert.Equal(new[] { firstBand, middleBand, lastBand },
            definition.Bands.Select(band => band.Id));
        Assert.All(biome.Decorations, decorator =>
        {
            definition.ValidateWeights(
                Assert.IsType<SurfaceHabitatWeights>(decorator.HabitatWeights));
            Assert.NotNull(decorator.Conditions);
        });

        var overworld = LoadOverworld();
        var roots = overworld.GeneratedSurfaceStructures
            .Where(root => root.Biome == biome.Id).ToArray();
        Assert.Equal(rootCount, roots.Length);
        Assert.All(roots.Where(root => root.Structure != "asteria:mountain_waterfall"),
            root => definition.ValidateWeights(
                Assert.IsType<SurfaceHabitatWeights>(root.HabitatWeights)));
        Assert.All(roots.Where(root => root.Structure == "asteria:mountain_waterfall"),
            root => Assert.Null(root.HabitatWeights));

        var a = new SurfaceHabitatField(95UL, [biome]);
        var b = new SurfaceHabitatField(95UL, [biome]);
        foreach (var (x, z) in new[] { (-193, 87), (-1, 0), (63, -72), (304, 151) })
        {
            Assert.Equal(a.Sample(biome.Id, x, z),
                         b.Sample(biome.Id, x, z));
            foreach (var root in roots.Where(root => root.HabitatWeights is not null))
                Assert.Equal(a.Weight(biome.Id, root.HabitatWeights, x, z),
                             b.Weight(biome.Id, root.HabitatWeights, x, z));
        }
    }

    [Theory]
    [InlineData("mountains", "asteria:stone", 112, 180)]
    [InlineData("mountain_belt", "asteria:stone", 115, 180)]
    [InlineData("alps", "asteria:snow", 145, 90)]
    [InlineData("gorge", "asteria:stone", 96, 150)]
    public void MountainSurfacePatchesAreOrganicDeterministicAndConstrainedByAltitudeAndSlope(
        string file, string originalBlock, int eligibleY, int ineligibleY)
    {
        var biome = LoadBiome(file);
        var patch = Assert.IsType<BiomeSurfacePatchDefinition>(
            biome.SurfaceLayers[0].Patch);
        var conditions = Assert.IsType<SurfacePlacementConditions>(patch.Conditions);
        Assert.True(conditions.Allows(new SurfacePlacementContext(eligibleY, 0)));
        Assert.False(conditions.Allows(new SurfacePlacementContext(ineligibleY, 0)));
        Assert.False(conditions.Allows(new SurfacePlacementContext(eligibleY, 20)));
        Assert.InRange(patch.Coverage, 0.15f, 0.35f);
        Assert.True(patch.WarpStrength > 0d);
        Assert.True(patch.Roughness > 0f);

        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var a = new BiomeSurfaceMaterialField(71UL, [biome], blocks);
        var b = new BiomeSurfaceMaterialField(71UL, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var original = blocks.GetId(originalBlock);
        var core = blocks.GetId(biome.SurfaceLayers[^1].Block);
        var seen = new HashSet<BlockRuntimeId>();
        var flat = new SurfacePlacementContext(eligibleY, 0);
        var excluded = new SurfacePlacementContext(ineligibleY, 0);
        var tooSteep = new SurfacePlacementContext(eligibleY, 20);
        for (var z = -192; z <= 192; z += 6)
        for (var x = -192; x <= 192; x += 6)
        {
            var eligible = a.BlockAt(sample, x, z, 0, flat);
            Assert.Equal(eligible, b.BlockAt(sample, x, z, 0, flat));
            seen.Add(eligible);
            Assert.Equal(original, a.BlockAt(sample, x, z, 0, excluded));
            Assert.Equal(original, a.BlockAt(sample, x, z, 0, tooSteep));
            Assert.Equal(core, a.BlockAt(sample, x, z, 3, flat));
        }
        Assert.True(seen.Contains(original));
        Assert.True(seen.Count > 1);
        Assert.Contains(patch.Blocks, block => seen.Contains(blocks.GetId(block)));
    }

    [Fact]
    public void TalusOutcropTemplatesUseStableStoneCobbleAndFullBiomeGroundFit()
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var variants = structures.ResolveReference("asteria:talus_outcrop");
        Assert.Equal(2, variants.Count);
        foreach (var structure in variants)
        {
            Assert.False(structure.Locatable);
            Assert.True(structure.Rotation);
            Assert.True(structure.Restrictions.RequiresDryGround);
            Assert.Equal(2, structure.Restrictions.MaxSlope);
            Assert.Equal(1f, structure.Restrictions.RequiredBiomeCoverage);
            Assert.Equal(StructureReplacePolicy.Terrain, structure.Generation.ReplacePolicy);
            Assert.Equal(StructureFluidPolicy.Forbid, structure.Generation.FluidPolicy);
            Assert.False(structure.Generation.ReserveSpace);
            Assert.Contains("rock", structure.ConflictGroups);
            Assert.Contains("asteria:stone", structure.Restrictions.GroundBlocks);
            Assert.Contains("asteria:gravel", structure.Restrictions.GroundBlocks);
            Assert.All(structure.Voxels, voxel =>
                Assert.Equal("asteria:stone_cobble", voxel.Block));
        }
        structures.ValidateBlocks(blocks);
    }

    [Fact]
    public void MountainStructuresFavorHabitatSpecificGeologyAndPreserveWaterfalls()
    {
        var roots = LoadOverworld().GeneratedSurfaceStructures;
        var middleHabitat = new Dictionary<string, string>
        {
            ["mountains"] = "talus_fields",
            ["mountain_belt"] = "boulder_runs",
            ["gorge"] = "rubble_slopes",
        };
        foreach (var (name, middle) in middleHabitat)
        {
            var biome = "asteria:overworld/" + name;
            var talus = Assert.Single(roots, root =>
                root.Biome == biome && root.Structure == "asteria:talus_outcrop");
            var cluster = Assert.Single(roots, root =>
                root.Biome == biome && root.Structure == "asteria:rock_cluster");
            Assert.True(talus.HabitatWeights!.For(middle) > 1f);
            Assert.True(cluster.HabitatWeights!.For(middle) > 1f);
        }

        var alpineIce = Assert.Single(roots, root =>
            root.Biome == "asteria:overworld/alps" &&
            root.Structure == "asteria:ice_outcrop");
        Assert.True(alpineIce.HabitatWeights!.For("glacial_ice") >
                    alpineIce.HabitatWeights.For("snowfields"));
        var alpineRock = Assert.Single(roots, root =>
            root.Biome == "asteria:overworld/alps" &&
            root.Structure == "asteria:rock_cluster");
        Assert.True(alpineRock.HabitatWeights!.For("wind_scoured_rock") >
                    alpineRock.HabitatWeights.For("snowfields"));

        foreach (var name in new[] { "mountains", "mountain_belt", "alps" })
        {
            var waterfall = Assert.Single(roots, root =>
                root.Biome == "asteria:overworld/" + name &&
                root.Structure == "asteria:mountain_waterfall");
            Assert.Null(waterfall.HabitatWeights);
        }
    }

    private static BiomeDefinition LoadBiome(string name) =>
        BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", name + ".json")));

    private static DimensionDefinition LoadOverworld() =>
        DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));

    private static IEnumerable<string> ReadJsonDirectory(string category) =>
        Directory.EnumerateFiles(Path.Combine(PackData, category), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
