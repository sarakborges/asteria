using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VolumeStructureContentTests
{
    [Fact]
    public void VolumeBiomesAuthorMultiBlockStructureFamiliesOnRealFaces()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        biomes.ValidateBlocks(blocks);
        structures.ValidateBlocks(blocks);
        var cave = biomes.Get("asteria:overworld/caverns");
        var islands = biomes.Get("asteria:overworld/floating_islands");

        Assert.Equal(VolumeBiomePlacement.CarvedVoid,
            cave.VolumeLayout!.Placement);
        Assert.Equal(VolumeBiomePlacement.Additive,
            islands.VolumeLayout!.Placement);
        Assert.Equal(3, cave.VolumeStructures.Count);
        Assert.Single(islands.VolumeStructures);
        Assert.Equal(6, cave.CaveSpikes.Count);
        Assert.Single(islands.Terrain3d!.Additive);

        foreach (var biome in new[] { cave, islands })
        foreach (var rule in biome.VolumeStructures)
        {
            Assert.InRange(rule.Spacing, 16, 256);
            Assert.InRange(rule.Chance, 0.1f, 1f);
            var variants = structures.ResolveReference(rule.Structure);
            Assert.InRange(variants.Count, 2, 3);
            Assert.All(variants, structure =>
            {
                Assert.False(structure.Rotation);
                Assert.Equal(0, structure.GroundAnchorY);
                Assert.Empty(structure.FluidVoxels);
                Assert.Empty(structure.ClearVoxels);
                Assert.Empty(structure.Connectors);
                Assert.True(structure.Voxels.Count >= 8);
                Assert.True(structure.Voxels.Max(voxel => voxel.Y) <= 16);
                Assert.All(structure.Voxels, voxel =>
                {
                    Assert.Equal(rule.SupportSurface ==
                                 DecorationSupportSurface.Floor
                                     ? 1 : -1,
                        Math.Sign(voxel.Y));
                    Assert.True(Math.Max(Math.Abs(voxel.X),
                        Math.Abs(voxel.Z)) <= 6);
                });
            });
        }
    }

    [Fact]
    public void AllCaveAndSkyLandmarksUseTheirBiomeAndCrystalLighting()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        foreach (var (group, count, block) in new[]
        {
            ("mushroom_glow_cavern", 3, "mushroom_cap_glow"),
            ("cluster_crystal_cavern", 2, "crystal_core_aether"),
            ("chandelier_cavern", 2, "crystal_core_aether"),
            ("arch_crystal_sky", 2, "crystal_core_aether"),
        })
        {
            var variants = structures.ResolveReference("asteria:" + group);
            Assert.Equal(count, variants.Count);
            Assert.All(variants, v => Assert.Contains(v.Voxels,
                voxel => voxel.Block == $"asteria:{block}"));
        }
        var crystal = blocks.GetDefinition(
            blocks.GetId("asteria:crystal_core_aether"));
        Assert.True(crystal.LightEmission.Blue > 0);
        Assert.True(crystal.IsCollidable);
        Assert.True(blocks.GetDefinition(
            blocks.GetId("asteria:mushroom_cap_glow")).LightEmission.IsDark);

    }

    [Fact]
    public void VolumeStructureRulesRejectInvalidBoundsAndSpacing()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BiomeVolumeStructureDefinition(
                "asteria:test", 12, .5f, 20, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BiomeVolumeStructureDefinition(
                "asteria:test", 24, .5f, -1, 100));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new BiomeVolumeStructureDefinition(
                "asteria:test", 24, 2f, 20, 100));
    }

    [Fact]
    public void DisablingStructuresDoesNotResolveVolumeTemplateGroups()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var dimension = DimensionRegistry.FromJson(ReadJson("dimensions"))
            .Get(DimensionId.Overworld);

        // Existing bare-world callers intentionally provide no structure
        // registry. Disabling spawnStructures must remain supported.
        var generator = new BiomeWorldGenerator(
            782UL, dimension, blocks, fluids, biomes,
            StructureRegistry.Empty, generation:
                new WorldGenerationOptions(spawnStructures: false));
        var chunk = generator.Materialize(new ChunkCoord(0, 0, 0));
        Assert.NotNull(chunk);
    }

    [Fact]
    public void ResumableVolumeStructureCursorMatchesBulkQueryWithTinyBatches()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        var structureSets = StructureSetRegistry.FromJson(
            ReadJson("structure_sets"));
        var dimension = DimensionRegistry.FromJson(ReadJson("dimensions"))
            .Get(DimensionId.Overworld);
        var generator = new BiomeWorldGenerator(
            181960897289965UL,
            dimension,
            blocks,
            fluids,
            biomes,
            structures,
            structureSets);
        var field = generator.VolumeStructures;
        var coord = new ChunkCoord(0, 2, 0);
        var accessRegion = new ChunkGenerationAccessRegion(
            AsteriaChunkGenerationPlan.Default,
            coord,
            ChunkGenerationStepId.VolumeStructures);
        var cursor = field.CreateChunkCursor(coord, accessRegion);
        Span<VolumeStructureVoxel> batch =
            stackalloc VolumeStructureVoxel[1];
        var actual = new List<VolumeStructureVoxel>();
        var advanceCount = 0;

        while (!cursor.IsComplete)
        {
            _ = cursor.Advance(
                batch,
                maximumCandidateEvaluations: 1,
                out var written,
                maximumAnchorSupportSamples: 1);
            Assert.InRange(written, 0, batch.Length);
            for (var index = 0; index < written; index++)
                actual.Add(batch[index]);

            advanceCount++;
            Assert.True(advanceCount < 100_000,
                "The volume-structure cursor did not reach completion.");
        }

        Assert.True(advanceCount > 1,
            "The selected cavern section should require multiple candidate steps.");
        Assert.True(cursor.AnchorSearchSlices > 1,
            "A support-Y search should resume across bounded cursor slices.");
        var expected = field.VoxelsForChunk(coord);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void VolumeStructureAnchorsAreSharedAcrossSectionsAndIndependentOfRequestOrder()
    {
        const ulong seed = 181960897289965UL;
        var field = CreateGenerator(seed).VolumeStructures;
        var upperCoord = new ChunkCoord(0, 2, 0);
        var lowerCoord = new ChunkCoord(0, 1, 0);

        var upperCursor = field.CreateChunkCursor(upperCoord);
        var upper = Drain(upperCursor, maximumAnchorSupportSamples: 1);
        Assert.True(upperCursor.AnchorSearchSlices > 1);

        // A completed column product is reused by another vertical section.
        var lowerCursor = field.CreateChunkCursor(lowerCoord);
        var lower = Drain(lowerCursor);
        Assert.Equal(0, lowerCursor.AnchorSearchSlices);

        // A fresh field requested in the opposite order produces identical
        // placements for both sections.
        var reversedField = CreateGenerator(seed).VolumeStructures;
        var reversedLower = Drain(reversedField.CreateChunkCursor(lowerCoord));
        var reversedUpper = Drain(reversedField.CreateChunkCursor(upperCoord));
        Assert.Equal(lower, reversedLower);
        Assert.Equal(upper, reversedUpper);
    }

    [Fact]
    public void VolumeStructureColumnAccessEnvelopeExpandsToAuthoredVerticalRange()
    {
        var field = CreateGenerator(181960897289965UL).VolumeStructures;
        var target = new ChunkCoord(12, 7, -8);
        var region = field.CreateColumnAccessRegion(target);
        var column = new ChunkCoord(target.X, 0, target.Z);
        var origin = VoxelCoordinates.ChunkOrigin(column);
        var maximumSectionOffset = region.ReadEnvelope.MaximumSectionOffset;

        Assert.Equal(column, region.TargetSection);
        Assert.Equal(ChunkGenerationStepId.VolumeStructurePlacements, region.StepId);
        Assert.Equal(1, region.ReadEnvelope.HorizontalRadius);
        Assert.Equal(0, region.ReadEnvelope.MinimumSectionOffset);
        Assert.True(maximumSectionOffset > 0);
        Assert.True(region.CanReadWorldPosition(
            origin.X,
            maximumSectionOffset * Chunk.Size,
            origin.Z));
        Assert.False(region.CanReadWorldPosition(
            origin.X,
            (maximumSectionOffset + 1) * Chunk.Size,
            origin.Z));
        Assert.False(region.CanReadWorldPosition(origin.X, -1, origin.Z));
    }

    private static BiomeWorldGenerator CreateGenerator(ulong seed)
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        var fluids = FluidRegistry.FromJson(ReadJson("fluids"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        var structureSets = StructureSetRegistry.FromJson(
            ReadJson("structure_sets"));
        var dimension = DimensionRegistry.FromJson(ReadJson("dimensions"))
            .Get(DimensionId.Overworld);
        return new BiomeWorldGenerator(
            seed, dimension, blocks, fluids, biomes, structures, structureSets);
    }

    private static IReadOnlyList<VolumeStructureVoxel> Drain(
        VolumeStructureField.ChunkCursor cursor,
        int maximumAnchorSupportSamples = 16)
    {
        var batch = new VolumeStructureVoxel[64];
        var results = new List<VolumeStructureVoxel>();
        var advances = 0;
        while (!cursor.IsComplete)
        {
            _ = cursor.Advance(
                batch,
                maximumCandidateEvaluations: 4,
                out var written,
                maximumAnchorSupportSamples: maximumAnchorSupportSamples);
            for (var index = 0; index < written; index++)
                results.Add(batch[index]);
            advances++;
            Assert.True(advances < 100_000,
                "The volume-structure column product did not complete.");
        }

        return results;
    }

    private static IEnumerable<string> ReadJson(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
