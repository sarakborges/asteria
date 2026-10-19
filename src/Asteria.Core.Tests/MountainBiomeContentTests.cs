using System.Text.Json.Nodes;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class MountainBiomeContentTests
{
    private static readonly string PackData =
        Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");

    [Theory]
    [InlineData("mountains", "foothill_shelves", "talus_fields", "exposed_ridges", 11)]
    [InlineData("mountain_belt", "sheltered_passes", "boulder_runs", "bare_crests", 12)]
    [InlineData("alps", "snowfields", "glacial_ice", "wind_scoured_rock", 10)]
    [InlineData("gorge", "valley_floor", "rubble_slopes", "high_rims", 6)]
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
        Assert.All(roots.Where(root => root.Structure != "asteria:waterfall_mountain"),
            root => definition.ValidateWeights(
                Assert.IsType<SurfaceHabitatWeights>(root.HabitatWeights)));
        Assert.All(roots.Where(root => root.Structure == "asteria:waterfall_mountain"),
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
        if (file is "mountains" or "mountain_belt")
            Assert.True(conditions.Allows(new SurfacePlacementContext(ineligibleY, 0)));
        else
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
            if (file is "mountains" or "mountain_belt")
                Assert.Equal(eligible, a.BlockAt(sample, x, z, 0, excluded));
            else
                Assert.Equal(original, a.BlockAt(sample, x, z, 0, excluded));
            Assert.Equal(original, a.BlockAt(sample, x, z, 0, tooSteep));
            Assert.Equal(core, a.BlockAt(sample, x, z, 3, flat));
        }
        Assert.True(seen.Contains(original));
        Assert.True(seen.Count > 1);
        Assert.Contains(patch.Blocks, block => seen.Contains(blocks.GetId(block)));
    }

    [Theory]
    [InlineData("mountains")]
    [InlineData("mountain_belt")]
    [InlineData("alps")]
    public void MountainLateralFacesUseRockInsteadOfTheTopSurfacePatch(string file)
    {
        var biome = LoadBiome(file);
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var materials = new BiomeSurfaceMaterialField(71UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");

        Assert.True(materials.HasDirectionalOverride(
            sample, BiomePaletteFace.Walls));
        Assert.True(materials.MaxVolumePaintDepth(sample) > 0);
        foreach (var (x, y, z) in new[]
        {
            (-129, 90, 47),
            (-1, 145, -65),
            (87, 112, 193),
        })
        {
            Assert.Equal(stone, materials.VolumeBlockAt(
                sample, x, y, z, BiomePaletteFace.Walls));
        }
    }

    [Fact]
    public void AlpineStrataBoundariesUseDeterministicBroadNoise()
    {
        var path = Path.Combine(PackData, "biomes", "alps.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var profile = root["palette"]!["default"]!.AsArray();
        profile[0]!.AsObject().Remove("patch");
        profile[0]!["block"] = "asteria:snow";
        profile[0]!["depth"] = 2;
        profile[0]!["boundaryNoise"] = 1;
        profile[1]!.AsObject().Remove("depth");
        profile[1]!.AsObject().Remove("boundaryNoise");
        profile[1]!["block"] = "asteria:stone";

        var biome = BiomeDefinitionJson.Parse(root.ToJsonString());
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var a = new BiomeSurfaceMaterialField(713UL, [biome], blocks);
        var b = new BiomeSurfaceMaterialField(713UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var seen = new HashSet<BlockRuntimeId>();
        var stone = blocks.GetId("asteria:stone");
        var snow = blocks.GetId("asteria:snow");

        for (var z = -384; z <= 384; z += 8)
        for (var x = -384; x <= 384; x += 8)
        {
            var material = a.BlockAt(sample, x, z, 1);
            Assert.Equal(material, b.BlockAt(sample, x, z, 1));
            seen.Add(material);
        }

        Assert.Contains(stone, seen);
        Assert.Contains(snow, seen);
        Assert.Subset(new HashSet<BlockRuntimeId> { stone, snow }, seen);
    }

    [Fact]
    public void TalusOutcropTemplatesUseStableStoneCobbleAndFullBiomeGroundFit()
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var variants = structures.ResolveReference("asteria:outcrop_talus");
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
                Assert.Equal("asteria:cobble_stone", voxel.Block));
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
                root.Biome == biome && root.Structure == "asteria:outcrop_talus");
            var cluster = Assert.Single(roots, root =>
                root.Biome == biome && root.Structure == "asteria:cluster_rock");
            Assert.True(talus.HabitatWeights!.For(middle) > 1f);
            Assert.True(cluster.HabitatWeights!.For(middle) > 1f);
        }

        var alpineIce = Assert.Single(roots, root =>
            root.Biome == "asteria:overworld/alps" &&
            root.Structure == "asteria:outcrop_ice");
        Assert.True(alpineIce.HabitatWeights!.For("glacial_ice") >
                    alpineIce.HabitatWeights.For("snowfields"));
        var alpineRock = Assert.Single(roots, root =>
            root.Biome == "asteria:overworld/alps" &&
            root.Structure == "asteria:cluster_rock");
        Assert.True(alpineRock.HabitatWeights!.For("wind_scoured_rock") >
                    alpineRock.HabitatWeights.For("snowfields"));

        foreach (var name in new[] { "mountains", "mountain_belt", "alps" })
        {
            var waterfall = Assert.Single(roots, root =>
                root.Biome == "asteria:overworld/" + name &&
                root.Structure == "asteria:waterfall_mountain");
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
