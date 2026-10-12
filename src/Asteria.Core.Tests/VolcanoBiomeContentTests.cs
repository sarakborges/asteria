using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VolcanoBiomeContentTests
{
    private static readonly string PackData =
        Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");

    [Fact]
    public void VolcanoUsesOneCalderaProfileForRimBasinAndLava()
    {
        var biome = LoadVolcano();
        var caldera = Assert.IsType<BiomeCalderaTerrainShapeDefinition>(
            biome.SurfaceTerrain!.Shape);
        Assert.Equal((12f, 72f, .27f, .08f, 58f),
            (caldera.BaseHeight, caldera.Height, caldera.CraterRadius,
                caldera.FloorRadius, caldera.CraterDepth));
        Assert.Equal(112f, caldera.FootprintRadius);
        Assert.Equal(40f, caldera.BiomeMargin);
        Assert.True(biome.SurfaceLayout!.RegionMin >=
            2 * (caldera.FootprintRadius + caldera.BiomeMargin));
        Assert.Equal("asteria:lava", caldera.FluidFill!.Fluid);
        Assert.Equal(54f, caldera.FluidFill.TopLevel);
        Assert.True(caldera.RimStrength < caldera.FloorStrength);
    }

    [Theory]
    [InlineData(17UL)]
    [InlineData(44UL)]
    [InlineData(731UL)]
    public void GeneratedCalderaAndItsFullSafetyMarginBelongToVolcanoBiome(ulong seed)
    {
        var volcano = LoadVolcano();
        var plains = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "plains.json")));
        var biomes = new BiomeRegistry([volcano, plains]);
        var field = new BiomeField(seed, [volcano.Id, plains.Id], biomes);
        var shape = Assert.IsType<BiomeCalderaTerrainShapeDefinition>(
            volcano.SurfaceTerrain!.Shape);
        var protectedRadius = shape.FootprintRadius + shape.BiomeMargin;

        // Discover a REAL root from the generated placement field, rather
        // than testing a fabricated strength or a standalone height profile.
        var bestStrength = -1f;
        var centerX = 0;
        var centerZ = 0;
        var sampler = field.CreateSampler();
        for (var z = -512; z <= 512; z += 16)
        for (var x = -512; x <= 512; x += 16)
        {
            var sample = sampler.Sample(x, z);
            if (sample.Primary != volcano.Id ||
                sample.PrimaryTerrainStrength <= bestStrength)
                continue;
            centerX = x;
            centerZ = z;
            bestStrength = sample.PrimaryTerrainStrength;
        }
        Assert.True(bestStrength > .98f,
            "Expected a complete caldera center within the generated biome map.");

        // Refine to the exact local maximum, including jittered seed centers.
        var roughX = centerX;
        var roughZ = centerZ;
        for (var z = roughZ - 16; z <= roughZ + 16; z++)
        for (var x = roughX - 16; x <= roughX + 16; x++)
        {
            var sample = sampler.Sample(x, z);
            if (sample.Primary != volcano.Id ||
                sample.PrimaryTerrainStrength <= bestStrength)
                continue;
            centerX = x;
            centerZ = z;
            bestStrength = sample.PrimaryTerrainStrength;
        }
        Assert.True(bestStrength > .9995f);

        // All spokes and all radial depths must remain FULLY owned by the
        // volcano, up through the empty biome buffer beyond the mountain.
        for (var spoke = 0; spoke < 24; spoke++)
        {
            var angle = spoke * Math.Tau / 24d;
            for (var radius = 0; radius < protectedRadius - 6; radius += 5)
            {
                var x = centerX + (int)Math.Round(radius * Math.Cos(angle));
                var z = centerZ + (int)Math.Round(radius * Math.Sin(angle));
                var sample = sampler.Sample(x, z);
                Assert.Equal(volcano.Id, sample.Primary);
                Assert.Equal(1f, sample.PrimaryWeight);
            }
        }
    }

    [Fact]
    public void RejectsCalderaWhoseMinimumBiomeSizeCannotContainItsMargin()
    {
        var original = File.ReadAllText(
            Path.Combine(PackData, "biomes", "volcano.json"));
        var tooSmall = original.Replace(
            "\"min\": 384", "\"min\": 128", StringComparison.Ordinal);
        Assert.NotEqual(original, tooSmall);
        var volcano = BiomeDefinitionJson.Parse(tooSmall);
        var plains = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "plains.json")));
        Assert.Throws<ArgumentException>(() =>
            new BiomeField(44UL, [volcano.Id, plains.Id],
                new BiomeRegistry([volcano, plains])));
    }

    [Fact]
    public void CalderaFormsAContinuousRaisedRingAndSubmergedFloor()
    {
        var volcano = LoadVolcano();
        var caldera = Assert.IsType<BiomeCalderaTerrainShapeDefinition>(
            volcano.SurfaceTerrain!.Shape);
        var profile = new SurfaceTerrainRule(volcano);
        const double lavaTop = 54d;
        var rims = new List<double>();

        for (ulong seed = 1; seed <= 7; seed++)
        for (var z = -200; z <= 200; z += 20)
        for (var x = -200; x <= 200; x += 20)
        {
            var outer = profile.HeightOffsetAt(seed, x, z, 0.35f);
            var rim = profile.HeightOffsetAt(
                seed, x, z, (float)caldera.RimStrength);
            var floor = profile.HeightOffsetAt(seed, x, z, 1f);
            Assert.True(outer < rim, "The mountain must rise to its rim.");
            Assert.True(rim > lavaTop + 2d, "The rim must confine the lava.");
            Assert.True(floor < lavaTop, "The crater floor must be submerged.");
            rims.Add(rim);
        }

        Assert.InRange(rims.Max() - rims.Min(), 0d, 0.1d);
    }

    [Fact]
    public void VolcanicHabitatsHaveDeterministicSharedDistributions()
    {
        var biome = LoadVolcano();
        var definition = Assert.IsType<SurfaceHabitatDefinition>(biome.SurfaceHabitats);
        Assert.Equal(new[] { "ash_aprons", "fractured_flanks", "hardened_highlands" },
            definition.Bands.Select(band => band.Id));
        Assert.InRange(definition.TransitionWidth, .05f, .2f);

        // Additive volcanic flora must not invalidate the pebble's
        // dedicated placement and habitat invariants.
        Assert.All(biome.Decorations, decoration =>
        {
            definition.ValidateWeights(
                Assert.IsType<SurfaceHabitatWeights>(decoration.HabitatWeights));
            Assert.NotNull(decoration.Conditions);
        });
        var pebble = Assert.Single(biome.Decorations,
            decoration => decoration.Block == "asteria:pebble");
        Assert.Equal("asteria:pebble", pebble.Block);
        Assert.NotNull(pebble.Conditions);
        Assert.True(pebble.HabitatWeights!.For("fractured_flanks") >
                    pebble.HabitatWeights.For("ash_aprons"));
        definition.ValidateWeights(pebble.HabitatWeights);

        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));
        var roots = dimension.GeneratedSurfaceStructures
            .Where(rule => rule.Biome == biome.Id).ToArray();
        Assert.True(roots.Length >= 4);
        Assert.Contains(roots, root =>
            root.Structure == "asteria:volcanic_charred_trees");
        Assert.Contains(roots, root =>
            root.Structure == "asteria:volcanic_sulfur_cluster");
        Assert.All(roots, root =>
        {
            definition.ValidateWeights(
                Assert.IsType<SurfaceHabitatWeights>(root.HabitatWeights));
            Assert.True(root.Jitter < root.Spacing / 2);
        });
        var spire = Assert.Single(roots,
            root => root.Structure == "asteria:basalt_spire");
        Assert.True(spire.HabitatWeights!.For("hardened_highlands") >
                    spire.HabitatWeights.For("fractured_flanks"));
        Assert.Equal(0f, spire.HabitatWeights.For("ash_aprons"));

        var a = new SurfaceHabitatField(37UL, [biome]);
        var b = new SurfaceHabitatField(37UL, [biome]);
        foreach (var (x, z) in new[] { (-213, 71), (0, 0), (137, -229), (327, 104) })
        {
            Assert.Equal(a.Sample(biome.Id, x, z), b.Sample(biome.Id, x, z));
            foreach (var root in roots)
                Assert.Equal(a.Weight(biome.Id, root.HabitatWeights, x, z),
                             b.Weight(biome.Id, root.HabitatWeights, x, z));
        }
    }

    [Fact]
    public void VolcanoHasOrganicShallowDepositsWithoutChangingBasaltCore()
    {
        var biome = LoadVolcano();
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var patch = Assert.IsType<BiomeSurfacePatchDefinition>(
            biome.SurfaceLayers[0].Patch);
        Assert.Equal(2u, biome.SurfaceLayers[0].Depth);
        Assert.Equal("asteria:basalt", biome.SurfaceLayers[^1].Block);
        Assert.Equal(new[] { "asteria:basalt_cobble", "asteria:gravel" }, patch.Blocks);
        Assert.True(patch.WarpStrength > 0d);
        Assert.True(patch.Roughness > 0f);
        var conditions = Assert.IsType<SurfacePlacementConditions>(patch.Conditions);
        Assert.True(conditions.Allows(new SurfacePlacementContext(112, 1)));
        Assert.False(conditions.Allows(new SurfacePlacementContext(155, 1)));
        Assert.False(conditions.Allows(new SurfacePlacementContext(112, 11)));

        var first = new BiomeSurfaceMaterialField(941UL, [biome], blocks);
        var second = new BiomeSurfaceMaterialField(941UL, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var basalt = blocks.GetId("asteria:basalt");
        var seen = new HashSet<BlockRuntimeId>();
        var eligible = new SurfacePlacementContext(112, 1);
        var tooHigh = new SurfacePlacementContext(155, 1);
        var tooSteep = new SurfacePlacementContext(112, 11);
        for (var z = -192; z <= 192; z += 5)
        for (var x = -192; x <= 192; x += 5)
        {
            var a = first.BlockAt(sample, x, z, 0, eligible);
            Assert.Equal(a, second.BlockAt(sample, x, z, 0, eligible));
            seen.Add(a);
            Assert.Equal(basalt, first.BlockAt(sample, x, z, 0, tooHigh));
            Assert.Equal(basalt, first.BlockAt(sample, x, z, 0, tooSteep));
            Assert.Equal(basalt, first.BlockAt(sample, x, z, 2, eligible));
        }
        Assert.Contains(basalt, seen);
        Assert.Contains(blocks.GetId("asteria:basalt_cobble"), seen);
        Assert.Contains(blocks.GetId("asteria:gravel"), seen);
    }

    [Theory]
    [InlineData("asteria:basalt_outcrop", "asteria:basalt_cobble", false)]
    [InlineData("asteria:basalt_boulder", "asteria:basalt", false)]
    [InlineData("asteria:basalt_spire", "asteria:basalt", true)]
    public void VolcanicRockTemplatesAreDryAndNeverGenerateLava(
        string groupId, string expectedBlock, bool raised)
    {
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var variants = structures.ResolveReference(groupId);
        Assert.Equal(2, variants.Count);
        foreach (var shape in variants)
        {
            Assert.False(shape.Locatable);
            Assert.True(shape.Rotation);
            Assert.True(shape.Restrictions.RequiresDryGround);
            Assert.Equal(1f, shape.Restrictions.RequiredBiomeCoverage);
            Assert.Equal(StructureFluidPolicy.Forbid, shape.Generation.FluidPolicy);
            Assert.Equal(StructureReplacePolicy.Terrain, shape.Generation.ReplacePolicy);
            Assert.False(shape.Generation.ReserveSpace);
            Assert.Contains("rock", shape.ConflictGroups);
            Assert.Contains("asteria:basalt", shape.Restrictions.GroundBlocks);
            Assert.Contains("asteria:basalt_cobble", shape.Restrictions.GroundBlocks);
            Assert.All(shape.Voxels, voxel =>
            {
                Assert.Equal(expectedBlock, voxel.Block);
                Assert.True(voxel.Y >= (raised ? 1 : 0));
            });
        }
        structures.ValidateBlocks(blocks);
    }

    [Fact]
    public void BasaltClusterExpandsOnlyBoundedDryOutcrops()
    {
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var sets = StructureSetRegistry.FromJson(ReadJsonDirectory("structure_sets"));
        sets.ValidateStructures(structures);
        var cluster = sets.Get("asteria:basalt_cluster");
        Assert.Contains("rock", cluster.ConflictGroups);
        Assert.False(cluster.ReserveSpace);
        Assert.Equal("asteria:basalt_outcrop", cluster.Elements[0].Structure);
        Assert.Equal("asteria:basalt_outcrop", cluster.Elements[1].Structure);
        Assert.Equal(1, cluster.Elements[1].Count.Min);
        Assert.Equal(2, cluster.Elements[1].Count.Max);
        Assert.InRange(cluster.Elements[1].Placement.Attempts, 1, 64);
        Assert.True(cluster.Elements[1].Placement.MaxDistance <= 20);
        Assert.True(cluster.Elements[1].Placement.MinSeparation >= 6);
    }

    private static BiomeDefinition LoadVolcano() =>
        BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "volcano.json")));

    private static IEnumerable<string> ReadJsonDirectory(string category) =>
        Directory.EnumerateFiles(Path.Combine(PackData, category), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
