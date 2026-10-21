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
    public void SlopeConditionedVolumePatchesRequireTerrainContext()
    {
        var path = Path.Combine(PackData, "biomes", "mountains.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var wallLayer = root["palette"]!["walls"]![0]!.AsObject();
        wallLayer["patch"] = new JsonObject
        {
            ["scale"] = 24,
            ["coverage"] = 1.0,
            ["roughness"] = 0.0,
            ["blocks"] = new JsonArray("asteria:gravel"),
            ["conditions"] = new JsonObject { ["maxSlope"] = 2 }
        };

        var biome = BiomeDefinitionJson.Parse(root.ToJsonString());
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var materials = new BiomeSurfaceMaterialField(71UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);

        Assert.Throws<InvalidOperationException>(() =>
            materials.VolumeBlockAt(
                sample, 17, 120, -31, BiomePaletteFace.Walls));
    }

    [Fact]
    public void VolumeMaterialPatchesAreDeterministicAcrossChunkBoundaries()
    {
        var (a, sample, stone, gravel) = CreateVolumePatchMaterials(719UL);
        var (b, _, _, _) = CreateVolumePatchMaterials(719UL);
        var coordinates = new List<(int X, int Y, int Z)>();
        foreach (var x in new[] { -33, -32, -17, -16, -1, 0, 15, 16, 17, 31, 32 })
        foreach (var y in new[] { 91, 97, 103, 109, 115, 121 })
        foreach (var z in new[] { -17, -16, -1, 0, 15, 16, 17 })
            coordinates.Add((x, y, z));

        var firstPass = coordinates.ToDictionary(
            p => p,
            p => a.VolumeBlockAt(sample, p.X, p.Y, p.Z, BiomePaletteFace.Walls));
        foreach (var p in coordinates.AsEnumerable().Reverse())
        {
            Assert.Equal(firstPass[p],
                b.VolumeBlockAt(sample, p.X, p.Y, p.Z, BiomePaletteFace.Walls));
        }

        Assert.Contains(firstPass.Values, block => block == stone);
        Assert.Contains(firstPass.Values, block => block == gravel);
    }

    [Fact]
    public void VolumeMaterialPatchesVaryInThreeDimensionsInsteadOfFormingHorizontalBands()
    {
        var (materials, sample, stone, gravel) = CreateVolumePatchMaterials(823UL);
        var gravelCountsByY = new List<int>();
        for (var y = 88; y < 136; y++)
        {
            var gravelCount = 0;
            var stoneCount = 0;
            for (var z = -24; z < 24; z += 2)
            for (var x = -24; x < 24; x += 2)
            {
                var block = materials.VolumeBlockAt(
                    sample, x, y, z, BiomePaletteFace.Walls);
                if (block == gravel) gravelCount++;
                if (block == stone) stoneCount++;
            }

            Assert.True(gravelCount > 0 && stoneCount > 0,
                $"Expected mixed 3D patch coverage at y={y}; gravel={gravelCount}, stone={stoneCount}.");
            gravelCountsByY.Add(gravelCount);
        }

        Assert.True(gravelCountsByY.Distinct().Count() > 1,
            "Every horizontal slice had identical patch coverage; this indicates horizontal banding.");
    }

    [Fact]
    public void InheritedWallMaterialsUseAuthoredLayersAsOrganicThreeDimensionalRegions()
    {
        var biome = LoadBiome("desert");
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var materials = new BiomeSurfaceMaterialField(1907UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var sand = blocks.GetId("asteria:sand");
        var sandstone = blocks.GetId("asteria:sandstone");
        var stone = blocks.GetId("asteria:stone");
        var observed = new HashSet<BlockRuntimeId>();
        var stoneCountsByY = new List<int>();

        for (var y = 72; y <= 152; y += 4)
        {
            var stoneCount = 0;
            for (var z = -96; z <= 96; z += 4)
            for (var x = -96; x <= 96; x += 4)
            {
                var block = materials.ExteriorWallBlockAt(sample, x, y, z);
                observed.Add(block);
                if (block == stone)
                    stoneCount++;
            }

            stoneCountsByY.Add(stoneCount);
        }

        Assert.Contains(sand, observed);
        Assert.Contains(sandstone, observed);
        Assert.Contains(stone, observed);
        Assert.True(stoneCountsByY.Distinct().Count() > 1,
            "Inherited wall materials must vary by Y through 3D regions, not fixed depth bands.");
    }

    [Fact]
    public void ExteriorWallsInheritThreeDimensionalPatchesBeneathUniformWallPalettes()
    {
        var path = Path.Combine(PackData, "biomes", "mountains.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var surfacePatch = root["palette"]!["default"]![0]!["patch"]!.AsObject();
        surfacePatch.Remove("weights");
        surfacePatch["scale"] = 12;
        surfacePatch["detailScale"] = 3;
        surfacePatch["coverage"] = 0.5;
        surfacePatch["roughness"] = 0.25;
        surfacePatch["blocks"] = new JsonArray("asteria:gravel");

        var biome = BiomeDefinitionJson.Parse(root.ToJsonString());
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var materials = new BiomeSurfaceMaterialField(823UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");
        var gravel = blocks.GetId("asteria:gravel");
        var seen = new HashSet<BlockRuntimeId>();
        var gravelCountsByY = new List<int>();

        for (var y = 88; y < 136; y++)
        {
            var gravelCount = 0;
            for (var z = -24; z < 24; z += 2)
            for (var x = -24; x < 24; x += 2)
            {
                var block = materials.ExteriorWallBlockAt(sample, x, y, z);
                seen.Add(block);
                if (block == gravel)
                    gravelCount++;
            }

            gravelCountsByY.Add(gravelCount);
        }

        Assert.Contains(stone, seen);
        Assert.Contains(gravel, seen);
        Assert.True(gravelCountsByY.Distinct().Count() > 1,
            "Exterior wall patch coverage must vary across Y instead of forming horizontal bands.");
    }

    private static (BiomeSurfaceMaterialField Materials, BiomeSample Sample,
        BlockRuntimeId Stone, BlockRuntimeId Gravel) CreateVolumePatchMaterials(ulong seed)
    {
        var path = Path.Combine(PackData, "biomes", "mountains.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var wallLayer = root["palette"]!["walls"]![0]!.AsObject();
        wallLayer["patch"] = new JsonObject
        {
            ["scale"] = 12,
            ["detailScale"] = 3,
            ["coverage"] = 0.5,
            ["roughness"] = 0.25,
            ["blocks"] = new JsonArray("asteria:gravel")
        };

        var biome = BiomeDefinitionJson.Parse(root.ToJsonString());
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var materials = new BiomeSurfaceMaterialField(seed, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        return (materials, sample,
            blocks.GetId("asteria:stone"),
            blocks.GetId("asteria:gravel"));
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
