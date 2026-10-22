using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SparseOverworldBiomeContentTests
{
    [Theory]
    [InlineData("arctic", "frozen_plain", "ice_fields", "rocky_ridges", 6)]
    [InlineData("desert", "open_dunes", "sand_flats", "sandstone_outcrops", 4)]
    [InlineData("wasteland", "barren_flats", "deadwood_pockets", "rocky_scrub", 9)]
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
    [InlineData("arctic", "asteria:ice", "asteria:gravel")]
    [InlineData("wasteland", "asteria:gravel", "asteria:sandstone")]
    public void GeologyMaterialsAreVariedAndChunkIndependent(
        string file, string firstAlternative, string secondAlternative)
    {
        var biome = LoadBiome(file);
        var geology = Assert.IsType<BiomeGeologyDefinition>(biome.Geology);
        Assert.Null(biome.SurfaceLayers[0].Patch);

        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var first = new BiomeSurfaceMaterialField(278UL, [biome], blocks);
        var second = new BiomeSurfaceMaterialField(278UL, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var seen = new HashSet<BlockRuntimeId>();
        for (var z = -192; z <= 192; z += 8)
        for (var x = -192; x <= 192; x += 8)
        {
            Assert.True(first.TryGeologyBlockAt(sample, x, 76, z, out var material));
            Assert.True(second.TryGeologyBlockAt(sample, x, 76, z, out var repeat));
            Assert.Equal(material, repeat);
            seen.Add(material);
        }

        Assert.Contains(blocks.GetId(geology.BaseBlock), seen);
        Assert.Contains(blocks.GetId(firstAlternative), seen);
        Assert.True(seen.Any(material => material != blocks.GetId(geology.BaseBlock)),
            $"Expected non-core geological deposits for {file}.");
    }

    [Fact]
    public void OceanKeepsThinSandCoverAndUsesThreeDimensionalGeologyBelowIt()
    {
        var biome = LoadBiome("ocean");
        var geology = Assert.IsType<BiomeGeologyDefinition>(biome.Geology);
        Assert.Equal(1u, geology.SurfaceCoverDepth);
        Assert.Equal("asteria:sand", biome.SurfaceLayers[0].Block);
        Assert.Equal(1u, biome.SurfaceLayers[0].Depth);
        Assert.Null(biome.SurfaceLayers[0].Patch);
        Assert.Equal("asteria:stone", biome.SurfaceLayers[1].Block);
        Assert.Null(biome.SurfaceLayers[1].Depth);

        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var first = new BiomeSurfaceMaterialField(377UL, [biome], blocks);
        var second = new BiomeSurfaceMaterialField(377UL, [biome], blocks);
        var sample = new BiomeSample(biome.Id,
            [new BiomeInfluence(biome.Id, 1f)]);
        var sand = blocks.GetId("asteria:sand");
        var seenAtSameAltitude = new HashSet<BlockRuntimeId>();
        var seenAlongVerticalColumn = new HashSet<BlockRuntimeId>();

        for (var z = -384; z <= 384; z += 12)
        for (var x = -384; x <= 384; x += 12)
        {
            Assert.Equal(sand, first.BlockAt(
                sample, x, z, 0, new SurfacePlacementContext(90, 0)));
            Assert.Equal(sand, first.BlockAt(
                sample, x, z, 0, new SurfacePlacementContext(70, 0)));
            Assert.True(first.TryGeologyBlockAt(sample, x, 82, z, out var material));
            Assert.True(second.TryGeologyBlockAt(sample, x, 82, z, out var repeat));
            Assert.Equal(material, repeat);
            seenAtSameAltitude.Add(material);
        }

        for (var y = 8; y <= 168; y += 4)
        {
            Assert.True(first.TryGeologyBlockAt(sample, 355, y, -230, out var material));
            seenAlongVerticalColumn.Add(material);
        }

        Assert.Contains(blocks.GetId("asteria:stone"), seenAtSameAltitude);
        Assert.Contains(blocks.GetId("asteria:sand"), seenAtSameAltitude);
        Assert.Contains(blocks.GetId("asteria:gravel"), seenAtSameAltitude);
        Assert.True(seenAtSameAltitude.Count > 2);
        Assert.True(seenAlongVerticalColumn.Count > 1);
    }

    [Fact]
    public void OceanGeologyDoesNotCollapseIntoStrongHorizontalOrVerticalBands()
    {
        var biome = LoadBiome("ocean");
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var field = new BiomeSurfaceMaterialField(
            7808508665006554090UL, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        const int step = 8;
        var horizontalTransitions = 0;
        var verticalTransitions = 0;
        var sampleCount = 0;

        for (var z = -128; z <= 128; z += 32)
        for (var y = 16; y <= 144; y += step)
        for (var x = -128; x <= 128; x += step)
        {
            Assert.True(field.TryGeologyBlockAt(sample, x, y, z, out var current));
            Assert.True(field.TryGeologyBlockAt(sample, x + step, y, z, out var right));
            Assert.True(field.TryGeologyBlockAt(sample, x, y + step, z, out var above));

            if (current != right)
                horizontalTransitions++;
            if (current != above)
                verticalTransitions++;
            sampleCount++;
        }

        Assert.True(horizontalTransitions > 0 && verticalTransitions > 0,
            $"Expected geological transitions in both axes; horizontal={horizontalTransitions}, vertical={verticalTransitions}.");
        var verticalToHorizontalRatio =
            verticalTransitions / (double)horizontalTransitions;
        Assert.InRange(verticalToHorizontalRatio, 0.5, 2.0);
        Assert.True(sampleCount > 5_000);
    }

    [Fact]
    public void OceanRegressionRegionMaterializationMatchesThreeDimensionalGeology()
    {
        const ulong seed = 7808508665006554090UL;
        const int centerX = 355;
        const int centerZ = -230;
        const int diagnosticY = 82;
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJsonDirectory("fluids"));
        var biomes = BiomeRegistry.FromJson(ReadJsonDirectory("biomes"));
        var structures = StructureRegistry.FromJson(ReadJsonDirectory("structures"));
        var structureSets = StructureSetRegistry.FromJson(ReadJsonDirectory("structure_sets"));
        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));
        var dimensionSeed = DimensionSeed.Derive(seed, dimension.Id);
        var generator = new BiomeWorldGenerator(
            dimensionSeed, dimension, blocks, fluids, biomes,
            structures, structureSets,
            new WorldGenerationOptions(
                spawnStructures: false, spawnCaves: false, spawnOceans: true));
        var ocean = biomes.Get("asteria:overworld/ocean");
        var geology = new BiomeSurfaceMaterialField(dimensionSeed, [ocean], blocks);
        var chunkCache = new Dictionary<ChunkCoord, Chunk>();
        var fixedAltitudeMaterials = new HashSet<BlockRuntimeId>();
        var materialsByDepth = new Dictionary<int, HashSet<BlockRuntimeId>>
        {
            [1] = [],
            [2] = [],
            [4] = [],
            [8] = [],
            [12] = [],
        };
        var checkedOceanVoxels = 0;

        BlockRuntimeId MaterializedBlock(int x, int y, int z)
        {
            var (coord, local) = VoxelCoordinates.FromWorld(x, Math.Max(0, y), z);
            if (!chunkCache.TryGetValue(coord, out var chunk))
            {
                chunk = generator.Materialize(coord);
                chunkCache.Add(coord, chunk);
            }

            return chunk.GetBlock(local.X, local.Y, local.Z);
        }

        void AssertMatchesGeology(int x, int y, int z, BiomeSample sample)
        {
            Assert.True(geology.TryGeologyBlockAt(sample, x, y, z, out var expected));
            var actual = MaterializedBlock(x, y, z);
            Assert.True(expected == actual,
                $"Geology mismatch at ({x},{y},{z}) in {sample.Primary}: " +
                $"expected {blocks.GetDefinition(expected).Id}, actual {blocks.GetDefinition(actual).Id}.");
            checkedOceanVoxels++;
        }

        for (var z = centerZ - 80; z <= centerZ + 80; z += 8)
        for (var x = centerX - 80; x <= centerX + 80; x += 8)
        {
            var sample = generator.Biomes.Sample(x, z);
            if (sample.Primary != ocean.Id)
                continue;

            if (diagnosticY >= 0 &&
                diagnosticY < generator.SurfaceHeight(x, z) &&
                generator.DensityAt(x, diagnosticY, z) >= 0d &&
                !generator.IsCaveVoidAt(x, diagnosticY, z))
            {
                AssertMatchesGeology(x, diagnosticY, z, sample);
                fixedAltitudeMaterials.Add(MaterializedBlock(x, diagnosticY, z));
            }

            var surfaceY = generator.SurfaceHeight(x, z);
            foreach (var depth in materialsByDepth.Keys.ToArray())
            {
                var y = surfaceY - depth;
                if (y < 0 || generator.DensityAt(x, y, z) < 0d ||
                    generator.IsCaveVoidAt(x, y, z))
                    continue;

                AssertMatchesGeology(x, y, z, sample);
                materialsByDepth[depth].Add(MaterializedBlock(x, y, z));
            }
        }

        Assert.True(checkedOceanVoxels >= 50,
            $"Expected a useful ocean sample near {centerX},{centerZ}; checked {checkedOceanVoxels} solid voxels.");
        Assert.True(fixedAltitudeMaterials.Count >= 2,
            $"The materialized ocean slice at Y={diagnosticY} had only {fixedAltitudeMaterials.Count} material(s).");
        foreach (var (depth, materials) in materialsByDepth)
        {
            Assert.True(materials.Count >= 3,
                $"Ocean seabed at depth {depth} had only {materials.Count} geological material(s).");
        }
    }

    [Fact]
    public void DesertKeepsSandAtTheSurfaceWithoutGravelPatchesOrPebbles()
    {
        var biome = LoadBiome("desert");
        var expectedDecorators = new[]
        {
            "asteria:grass_dune",
            "asteria:thornbush",
            "asteria:yucca",
            "asteria:spike_sandstone",
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
    [InlineData("asteria:outcrop_ice", "asteria:ice", "asteria:snow", false)]
    [InlineData("asteria:outcrop_sandstone", "asteria:sandstone", "asteria:sand", false)]
    [InlineData("asteria:spire_sandstone", "asteria:sandstone", "asteria:sand", true)]
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
    public void WastelandDeadwoodKeepsFantasyLeavesWithoutReplacingDrySoil()
    {
        var structures = LoadStructures();
        foreach (var group in new[] { "asteria:snag_wasteland", "asteria:log_wasteland_fallen" })
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
                Assert.Contains(structure.Voxels,
                    voxel => voxel.Block == "asteria:leaf_oak");
                Assert.All(structure.Voxels, voxel =>
                {
                    Assert.True(voxel.Y >= 1);
                    if (voxel.Block == "asteria:leaf_oak")
                        Assert.Equal(BlockOrientation.Y, voxel.Orientation);
                    else
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
            rule.Structure == "asteria:outcrop_ice");
        Assert.True(arcticIce.HabitatWeights!.For("ice_fields") >
                    arcticIce.HabitatWeights.For("frozen_plain"));

        var desertSpire = Assert.Single(roots, rule =>
            rule.Biome == "asteria:overworld/desert" &&
            rule.Structure == "asteria:spire_sandstone");
        Assert.True(desertSpire.HabitatWeights!.For("sandstone_outcrops") >
                    desertSpire.HabitatWeights.For("open_dunes"));

        var deadwood = Assert.Single(roots, rule =>
            rule.Biome == "asteria:overworld/wasteland" &&
            rule.Structure == "asteria:snag_wasteland");
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
