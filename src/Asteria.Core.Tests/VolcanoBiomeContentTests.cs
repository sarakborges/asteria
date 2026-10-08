using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VolcanoBiomeContentTests
{
    private static readonly string PackData =
        Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");

    [Fact]
    public void VolcanoCraterLavaSettingsRemainAuthoredAndUnchanged()
    {
        var biome = LoadVolcano();
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(PackData, "biomes", "volcano.json")));
        var terrain = document.RootElement.GetProperty("surfaceTerrain");
        Assert.Equal("cone", terrain.GetProperty("type").GetString());
        Assert.Equal(12, terrain.GetProperty("baseHeight").GetInt32());
        Assert.Equal(72, terrain.GetProperty("height").GetInt32());
        var crater = terrain.GetProperty("crater");
        Assert.Equal(38, crater.GetProperty("depth").GetInt32());
        Assert.Equal(.14, crater.GetProperty("radius").GetDouble(), 5);
        var fill = crater.GetProperty("fluidFill");
        Assert.Equal("asteria:lava", fill.GetProperty("fluid").GetString());
        Assert.Equal(.91, fill.GetProperty("minimumStrength").GetDouble(), 5);
        Assert.Equal(54, fill.GetProperty("topLevel").GetInt32());
        var spill = fill.GetProperty("spill");
        Assert.Equal(.56, spill.GetProperty("minimumStrength").GetDouble(), 5);
        Assert.Equal(.9, spill.GetProperty("maximumStrength").GetDouble(), 5);
        Assert.Equal(6, spill.GetProperty("level").GetInt32());
        Assert.NotNull(biome.SurfaceTerrain!.Crater!.FluidFill);
    }

    [Fact]
    public void VolcanicHabitatsHaveDeterministicSharedDistributions()
    {
        var biome = LoadVolcano();
        var definition = Assert.IsType<SurfaceHabitatDefinition>(biome.SurfaceHabitats);
        Assert.Equal(new[] { "ash_aprons", "fractured_flanks", "hardened_highlands" },
            definition.Bands.Select(band => band.Id));
        Assert.InRange(definition.TransitionWidth, .05f, .2f);

        var pebble = Assert.Single(biome.Decorations);
        Assert.Equal("asteria:pebble", pebble.Block);
        Assert.NotNull(pebble.Conditions);
        Assert.True(pebble.HabitatWeights!.For("fractured_flanks") >
                    pebble.HabitatWeights.For("ash_aprons"));
        definition.ValidateWeights(pebble.HabitatWeights);

        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));
        var roots = dimension.GeneratedSurfaceStructures
            .Where(rule => rule.Biome == biome.Id).ToArray();
        Assert.Equal(4, roots.Length);
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
