using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class VerticalBiomeContentTests
{
    private static readonly string PackData = Path.Combine(
        AppContext.BaseDirectory, "packs", "default", "data");

    [Fact]
    public void OceanExposesOrganicSeabedWithoutChangingItsUnderlyingLayers()
    {
        var blocks = BlockRegistry.FromJson(ReadFiles("blocks"));
        var ocean = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "ocean.json")));
        var patch = Assert.IsType<BiomeSurfacePatchDefinition>(
            ocean.SurfaceLayers[0].Patch);
        Assert.Equal("asteria:sand", ocean.SurfaceLayers[0].Block);
        Assert.Equal(4u, ocean.SurfaceLayers[0].Depth);
        Assert.Equal("asteria:gravel", ocean.SurfaceLayers[1].Block);
        Assert.Equal("asteria:stone", ocean.SurfaceLayers[2].Block);
        Assert.Equal(new[] { "asteria:gravel", "asteria:clay", "asteria:stone_cobble" },
            patch.Blocks);
        Assert.True(patch.WarpStrength > 0d);

        var a = new BiomeSurfaceMaterialField(217UL, [ocean], blocks);
        var b = new BiomeSurfaceMaterialField(217UL, [ocean], blocks);
        var sample = new BiomeSample(ocean.Id,
            [new BiomeInfluence(ocean.Id, 1f)]);
        var seen = new HashSet<BlockRuntimeId>();
        for (var z = -192; z <= 192; z += 5)
        for (var x = -192; x <= 192; x += 5)
        {
            var material = a.BlockAt(sample, x, z, 0);
            Assert.Equal(material, b.BlockAt(sample, x, z, 0));
            Assert.Equal(blocks.GetId("asteria:gravel"),
                a.BlockAt(sample, x, z, 4));
            Assert.Equal(blocks.GetId("asteria:stone"),
                a.BlockAt(sample, x, z, 6));
            seen.Add(material);
        }
        Assert.Contains(blocks.GetId("asteria:sand"), seen);
        Assert.Contains(blocks.GetId("asteria:gravel"), seen);
        Assert.Contains(blocks.GetId("asteria:clay"), seen);
        Assert.Contains(blocks.GetId("asteria:stone_cobble"), seen);
    }

    [Fact]
    public void OceanRockTemplatesDisplaceOnlyOccupiedFluidVoxels()
    {
        var blocks = BlockRegistry.FromJson(ReadFiles("blocks"));
        var structures = StructureRegistry.FromJson(ReadFiles("structures"));
        var sets = StructureSetRegistry.FromJson(ReadFiles("structure_sets"));
        structures.ValidateBlocks(blocks);
        sets.ValidateStructures(structures);
        var variants = structures.ResolveReference("asteria:ocean_rock");
        Assert.Equal(2, variants.Count);
        Assert.All(variants, structure =>
        {
            Assert.False(structure.Locatable);
            Assert.False(structure.Restrictions.RequiresDryGround);
            Assert.Equal(1f, structure.Restrictions.RequiredBiomeCoverage);
            Assert.Equal(StructureFluidPolicy.Displace, structure.Generation.FluidPolicy);
            Assert.Equal(StructureReplacePolicy.Terrain, structure.Generation.ReplacePolicy);
            Assert.Contains("asteria:sand", structure.Restrictions.GroundBlocks);
            Assert.Contains("asteria:clay", structure.Restrictions.GroundBlocks);
            Assert.All(structure.Voxels, voxel =>
                Assert.Equal("asteria:stone_cobble", voxel.Block));
        });

        var cluster = sets.Get("asteria:ocean_rock_cluster");
        Assert.False(cluster.ReserveSpace);
        Assert.Contains("rock", cluster.ConflictGroups);
        Assert.Equal("asteria:ocean_rock", cluster.Elements[0].Structure);
        Assert.Equal("asteria:ocean_rock", cluster.Elements[1].Structure);
        Assert.Equal(1, cluster.Elements[1].Count.Min);
        Assert.Equal(2, cluster.Elements[1].Count.Max);

        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));
        var oceanRoots = dimension.GeneratedSurfaceStructures.Where(
            r => r.Biome == "asteria:overworld/ocean").ToArray();
        Assert.Equal(3, oceanRoots.Length);
        Assert.Single(oceanRoots, r =>
            r.Structure == "asteria:river_ocean_mouth" &&
            r.Placement == DimensionGeneratedSurfaceStructurePlacement.BiomeMargin);
        var ocean = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "ocean.json")));
        Assert.Equal(new[] { "sand_flats", "gravel_banks", "rocky_reefs" },
            ocean.SurfaceHabitats!.Bands.Select(b => b.Id));
        foreach (var root in oceanRoots.Where(r => r.HabitatWeights is not null))
        {
            ocean.SurfaceHabitats.ValidateWeights(root.HabitatWeights);
            Assert.True(root.HabitatWeights!.For("rocky_reefs") >
                        root.HabitatWeights.For("sand_flats"));
        }
    }

    [Fact]
    public void CaveAndVolumeDecoratorsUseThreeDimensionalRollsAndTheirOwnGround()
    {
        var blocks = BlockRegistry.FromJson(ReadFiles("blocks"));
        var cave = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "caverns.json")));
        var floating = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "floating_islands.json")));
        Assert.Null(cave.SurfaceLayout);
        Assert.NotNull(cave.VolumeLayout);
        Assert.Equal(VolumeBiomePlacement.CarvedVoid, cave.VolumeLayout.Placement);
        Assert.NotEmpty(cave.SurfaceLayers);
        Assert.NotNull(cave.SurfaceLayers[0].Patch);
        Assert.Null(floating.SurfaceLayout);
        Assert.NotNull(floating.VolumeLayout);
        Assert.Equal(VolumeBiomePlacement.Additive, floating.VolumeLayout.Placement);
        Assert.Null(cave.SurfaceHabitats);
        Assert.Null(floating.SurfaceHabitats);
        Assert.Equal(3, cave.Decorations.Count);
        Assert.Equal(3, floating.Decorations.Count);

        var field = new SurfaceDecorationField(119UL, [cave, floating], blocks);
        Assert.True(field.HasVerticalDecorations);
        Assert.True(field.HasVerticalDecorationsFor(cave.Id));
        Assert.True(field.HasVerticalDecorationsFor(floating.Id));
        var caveSample = new BiomeSample(cave.Id,
            [new BiomeInfluence(cave.Id, 1f)]);
        var islandSample = new BiomeSample(floating.Id,
            [new BiomeInfluence(floating.Id, 1f)]);
        var stone = blocks.GetId("asteria:stone");
        var grassBlock = blocks.GetId("asteria:grass_block");
        var sightings = 0;
        var islandSightings = 0;
        var variationAcrossY = 0;
        for (var z = -64; z <= 64; z += 2)
        for (var x = -64; x <= 64; x += 2)
        {
            var caveAt = field.BlockAt(caveSample, stone, x, z,
                new SurfacePlacementContext(70, 0), verticalY:70);
            Assert.Equal(caveAt, field.BlockAt(caveSample, stone, x, z,
                new SurfacePlacementContext(70, 0), verticalY:70));
            if (!caveAt.IsAir) sightings++;

            var islandAt = field.BlockAt(islandSample, grassBlock, x, z,
                new SurfacePlacementContext(240, 0), verticalY:240);
            var higher = field.BlockAt(islandSample, grassBlock, x, z,
                new SurfacePlacementContext(241, 0), verticalY:241);
            if (!islandAt.IsAir) islandSightings++;
            if (islandAt != higher) variationAcrossY++;
            Assert.Equal(BlockRuntimeId.Air, field.BlockAt(islandSample, stone, x, z,
                new SurfacePlacementContext(240, 0), verticalY:240));
        }

        Assert.True(sightings > 0);
        Assert.True(islandSightings > 0);
        Assert.True(variationAcrossY > 0);
    }

    [Fact]
    public void SharedPaletteSamplesVolumePatchesInThreeDimensions()
    {
        var blocks = BlockRegistry.FromJson(ReadFiles("blocks"));
        var cave = BiomeDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "biomes", "caverns.json")));
        var field = new BiomeSurfaceMaterialField(418UL, [cave], blocks);
        var again = new BiomeSurfaceMaterialField(418UL, [cave], blocks);
        var biome = new BiomeSample(cave.Id,
            [new BiomeInfluence(cave.Id, 1f)]);

        var seen = new HashSet<BlockRuntimeId>();
        var verticalVariation = 0;
        for (var z = -72; z <= 72; z += 8)
        for (var x = -72; x <= 72; x += 8)
        {
            BlockRuntimeId? previous = null;
            for (var y = 16; y <= 112; y += 8)
            {
                var selected = field.VolumeBlockAt(biome, x, y, z);
                Assert.Equal(selected, again.VolumeBlockAt(biome, x, y, z));
                seen.Add(selected);
                if (previous is { } old && old != selected)
                    verticalVariation++;
                previous = selected;
            }
        }

        Assert.True(seen.Count >= 3,
            "Volume palette should expose several authored materials.");
        Assert.True(verticalVariation > 0,
            "Cave wall materials must vary in Y, not repeat 2D surface stripes.");
        Assert.Equal(blocks.GetId("asteria:stone"),
            field.BlockAt(biome, 0, 0, 20));
    }

    [Fact]
    public void CarvedVoidVolumeIdentityDoesNotOverwriteAdditiveVolumes()
    {
        var biomes = new BiomeRegistry(ReadFiles("biomes")
            .Select(BiomeDefinitionJson.Parse));
        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(
            Path.Combine(PackData, "dimensions", "overworld.json")));
        var volume = new VolumeBiomeField(514UL, dimension, biomes);
        Assert.True(volume.HasBiomes);
        Assert.NotNull(volume.SampleCave(0, 0));
        Assert.Equal("asteria:overworld/caverns",
            volume.SampleCave(0, 0)!.Primary);

        // Cave ownership does not turn a surface or additive formation
        // into a Caverns volume at arbitrary vertical coordinates.
        var high = volume.Sample(0, 240, 0);
        Assert.True(high is null ||
                    high.Primary == "asteria:overworld/floating_islands");
        Assert.Null(volume.Sample(0, 32, 0));
    }

    [Fact]
    public void VolumePlacementContractRejectsInvalidGeometryCombination()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeDefinition(
                "asteria:test/carved", null, null,
                surfaceLayers: [new BiomeSurfaceLayerDefinition("asteria:stone")],
                volumeLayout: new BiomeVolumeLayoutDefinition(
                    placement: VolumeBiomePlacement.CarvedVoid),
                terrain3d: new BiomeTerrain3dDefinition(
                    [new BiomeAdditiveDensityDefinition(
                        180, 220, 64, 20, 0.5f, 0.2f, 20f)])));

        Assert.Throws<FormatException>(() =>
            BiomeDefinitionJson.Parse(
                """
                {
                  "id": "asteria:test/invalid",
                  "volumeLayout": {"placement": "nonexistent"},
                  "surfaceLayers": [{"block": "asteria:stone"}]
                }
                """));
    }

    private static IEnumerable<string> ReadFiles(string folder) =>
        Directory.EnumerateFiles(
            Path.Combine(PackData, folder), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
