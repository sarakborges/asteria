using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SparseOverworldBiomeContentTests
{
    [Theory]
    [InlineData("arctic", "frozen_plain", "ice_fields", "rocky_ridges", 6)]
    [InlineData("desert", "open_dunes", "sand_flats", "sandstone_outcrops", 5)]
    [InlineData("wasteland", "barren_flats", "deadwood_pockets", "rocky_scrub", 7)]
    public void AuthoredBiomesHaveDeterministicHabitatRegionsAndWeightedRoots(
        string file, string first, string second, string third, int rootCount)
    {
        var biome = LoadBiome(file);
        var definition = Assert.IsType<SurfaceHabitatDefinition>(biome.SurfaceHabitats);
        Assert.Equal(new[] { first, second, third },
            definition.Bands.Select(band => band.Id));
        Assert.All(biome.Decorations, decoration =>
            definition.ValidateWeights(Assert.IsType<SurfaceHabitatWeights>(
                decoration.HabitatWeights)));

        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));
        var roots = dimension.GeneratedSurfaceStructures
            .Where(rule => rule.Biome == biome.Id).ToArray();
        Assert.Equal(rootCount, roots.Length);
        Assert.All(roots, root =>
        {
            definition.ValidateWeights(
                Assert.IsType<SurfaceHabitatWeights>(root.HabitatWeights));
            Assert.InRange(root.Jitter, 0, root.Spacing / 2);
        });

        var firstField = new SurfaceHabitatField(377UL, [biome]);
        var secondField = new SurfaceHabitatField(377UL, [biome]);
        foreach (var (x, z) in new[] { (-213, -71), (-1, 0), (31, 84), (284, -291) })
        {
            Assert.Equal(firstField.Sample(biome.Id, x, z),
                         secondField.Sample(biome.Id, x, z));
            foreach (var root in roots)
                Assert.Equal(firstField.Weight(biome.Id, root.HabitatWeights, x, z),
                             secondField.Weight(biome.Id, root.HabitatWeights, x, z));
        }
    }

    [Theory]
    [InlineData("arctic", "asteria:snow", "asteria:ice", "asteria:gravel")]
    [InlineData("wasteland", "asteria:dirt", "asteria:gravel", "asteria:stone")]
    public void OrganicSurfacePatchesAreVariedAndChunkIndependent(
        string file, string baseBlock, string firstAlternative, string secondAlternative)
    {
        var biome = LoadBiome(file);
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var patch = Assert.IsType<BiomeSurfacePatchDefinition>(
            biome.SurfaceLayers[0].Patch);
        Assert.True(patch.WarpStrength > 0d);
        Assert.True(patch.Roughness > 0f);
        Assert.InRange(patch.Coverage, 0.1f, 0.35f);
        var first = new BiomeSurfaceMaterialField(278UL, [biome], blocks);
        var second = new BiomeSurfaceMaterialField(278UL, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var seen = new HashSet<BlockRuntimeId>();
        for (var z = -192; z <= 192; z += 4)
        {
            for (var x = -192; x <= 192; x += 4)
            {
                var material = first.BlockAt(sample, x, z, 0);
                Assert.Equal(material, second.BlockAt(sample, x, z, 0));
                seen.Add(material);
            }
        }

        Assert.Contains(blocks.GetId(baseBlock), seen);
        Assert.Contains(blocks.GetId(firstAlternative), seen);
        Assert.Contains(blocks.GetId(secondAlternative), seen);
    }

    [Fact]
    public void OceanKeepsBeachSandWhileDeepSeabedPatchesRemainAuthored()
    {
        var biome = LoadBiome("ocean");
        var patch = Assert.IsType<BiomeSurfacePatchDefinition>(
            biome.Palette.Default[0].Patch);
        Assert.Equal(86, Assert.IsType<SurfacePlacementConditions>(
            patch.Conditions).MaxY);
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var field = new BiomeSurfaceMaterialField(377UL, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var shallow = new SurfacePlacementContext(90, 0);
        var deep = new SurfacePlacementContext(70, 0);
        var seen = new HashSet<BlockRuntimeId>();
        for (var z = -192; z <= 192; z += 6)
        for (var x = -192; x <= 192; x += 6)
        {
            Assert.Equal(blocks.GetId("asteria:sand"),
                field.BlockAt(sample, x, z, 0, shallow));
            seen.Add(field.BlockAt(sample, x, z, 0, deep));
        }

        Assert.Contains(blocks.GetId("asteria:sand"), seen);
        Assert.Contains(blocks.GetId("asteria:gravel"), seen);
        Assert.True(seen.Count > 1,
            "Deep ocean remains diversified below the sandy beach.");
    }

    [Fact]
    public void DesertKeepsSandAtTheSurfaceWithoutGravelPatchesOrPebbles()
    {
        var biome = LoadBiome("desert");
        var expectedDecorators = new[]
        {
            "asteria:desert_dune_grass",
            "asteria:desert_thornbush",
            "asteria:desert_yucca",
            "asteria:desert_sandstone_spur",
        };
        Assert.Equal(expectedDecorators,
            biome.Decorations.Select(decoration => decoration.Block));
        Assert.All(biome.Decorations, decoration =>
        {
            Assert.Equal(new[] { "asteria:sand" }, decoration.SurfaceBlocks);
            Assert.DoesNotContain("asteria:gravel", decoration.SurfaceBlocks);
            Assert.DoesNotContain("asteria:pebble", decoration.SurfaceBlocks);
        });
        Assert.Equal("asteria:sand", biome.Palette.Default[0].Block);
        Assert.Null(biome.Palette.Default[0].Patch);
        Assert.Equal("asteria:sandstone", biome.Palette.Default[1].Block);
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var materials = new BiomeSurfaceMaterialField(278UL, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        for (var z = -192; z <= 192; z += 16)
        for (var x = -192; x <= 192; x += 16)
        {
            Assert.Equal(blocks.GetId("asteria:sand"),
                materials.BlockAt(sample, x, z, 0));
            Assert.Equal(blocks.GetId("asteria:sandstone"),
                materials.BlockAt(sample, x, z, 8));
        }
    }

    [Theory]
    [InlineData("asteria:ice_outcrop", "asteria:ice", "asteria:snow", false)]
    [InlineData("asteria:sandstone_outcrop", "asteria:sandstone", "asteria:sand", false)]
    [InlineData("asteria:sandstone_spire", "asteria:sandstone", "asteria:sand", true)]
    public void ColdAndDesertRockTemplatesPreserveAuthoredSurfaceConstraints(
        string groupId, string material, string ground, bool raised)
    {
        var structures = LoadStructures();
        var variants = structures.ResolveReference(groupId);
        Assert.Equal(2, variants.Count);
        Assert.All(variants, structure =>
        {
            Assert.False(structure.Locatable);
            Assert.True(structure.Rotation);
            Assert.True(structure.Restrictions.RequiresDryGround);
            Assert.Equal(1f, structure.Restrictions.RequiredBiomeCoverage);
            Assert.Equal(1, structure.Restrictions.MaxSlope);
            Assert.Equal(StructureFluidPolicy.Forbid, structure.Generation.FluidPolicy);
            Assert.Equal(StructureReplacePolicy.Terrain, structure.Generation.ReplacePolicy);
            Assert.False(structure.Generation.ReserveSpace);
            Assert.Contains(ground, structure.Restrictions.GroundBlocks);
            Assert.Contains("rock", structure.ConflictGroups);
            Assert.All(structure.Voxels, voxel =>
            {
                Assert.Equal(material, voxel.Block);
                Assert.True(voxel.Y >= (raised ? 1 : 0));
            });
        });
    }

    [Fact]
    public void WastelandDeadwoodNeverReplacesDrySoilWithGrassOrLeaves()
    {
        var structures = LoadStructures();
        foreach (var group in new[] { "asteria:wasteland_snag", "asteria:fallen_log_wasteland" })
        {
            var variants = structures.ResolveReference(group);
            Assert.Equal(2, variants.Count);
            Assert.All(variants, structure =>
            {
                Assert.False(structure.Locatable);
                Assert.True(structure.Rotation);
                Assert.Equal(0, structure.GroundAnchorYOffset);
                Assert.Equal(1f, structure.Restrictions.RequiredBiomeCoverage);
                Assert.Equal(StructureFluidPolicy.Forbid, structure.Generation.FluidPolicy);
                Assert.Equal(StructureReplacePolicy.Terrain, structure.Generation.ReplacePolicy);
                Assert.Contains("asteria:dirt", structure.Restrictions.GroundBlocks);
                Assert.Contains("asteria:gravel", structure.Restrictions.GroundBlocks);
                Assert.All(structure.Voxels, voxel =>
                {
                    Assert.True(voxel.Y >= 1);
                    Assert.StartsWith("asteria:log_oak", voxel.Block);
                });
            });
        }
    }

    [Fact]
    public void RockAndDeadwoodRootsFavorDifferentHabitats()
    {
        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));
        var roots = dimension.GeneratedSurfaceStructures;
        var arcticIce = Assert.Single(roots, rule =>
            rule.Biome == "asteria:overworld/arctic" &&
            rule.Structure == "asteria:ice_outcrop");
        Assert.True(arcticIce.HabitatWeights!.For("ice_fields") >
                    arcticIce.HabitatWeights.For("frozen_plain"));

        var desertSpire = Assert.Single(roots, rule =>
            rule.Biome == "asteria:overworld/desert" &&
            rule.Structure == "asteria:sandstone_spire");
        Assert.True(desertSpire.HabitatWeights!.For("sandstone_outcrops") >
                    desertSpire.HabitatWeights.For("open_dunes"));

        var deadwood = Assert.Single(roots, rule =>
            rule.Biome == "asteria:overworld/wasteland" &&
            rule.Structure == "asteria:wasteland_snag");
        Assert.True(deadwood.HabitatWeights!.For("deadwood_pockets") >
                    deadwood.HabitatWeights.For("barren_flats"));
        var stones = roots.Where(rule =>
            rule.Biome == "asteria:overworld/wasteland" &&
            rule.Structure.StartsWith("asteria:boulder_", StringComparison.Ordinal));
        Assert.Equal(4, stones.Count());
        Assert.All(stones, rule =>
            Assert.True(rule.HabitatWeights!.For("rocky_scrub") >
                        rule.HabitatWeights.For("deadwood_pockets")));
    }

    private static readonly string PackData =
        Path.Combine(AppContext.BaseDirectory, "packs", "default", "data");

    private static BiomeDefinition LoadBiome(string name) =>
        BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", name + ".json")));

    private static StructureRegistry LoadStructures() =>
        StructureRegistry.FromJson(ReadJsonDirectory("structures"));

    private static IEnumerable<string> ReadJsonDirectory(string folder) =>
        Directory.EnumerateFiles(Path.Combine(PackData, folder), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
