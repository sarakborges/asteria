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

        Assert.Equal(86, patch.Conditions!.MaxY);
        var a = new BiomeSurfaceMaterialField(217UL, [ocean], blocks);
        var b = new BiomeSurfaceMaterialField(217UL, [ocean], blocks);
        var sample = new BiomeSample(ocean.Id,
            [new BiomeInfluence(ocean.Id, 1f)]);
        var seen = new HashSet<BlockRuntimeId>();
        for (var z = -192; z <= 192; z += 5)
        for (var x = -192; x <= 192; x += 5)
        {
            var deep = new SurfacePlacementContext(70, 0);
            var shore = new SurfacePlacementContext(90, 0);
            var material = a.BlockAt(sample, x, z, 0, deep);
            Assert.Equal(material, b.BlockAt(sample, x, z, 0, deep));
            Assert.Equal(blocks.GetId("asteria:sand"),
                a.BlockAt(sample, x, z, 0, shore));
            Assert.Equal(blocks.GetId("asteria:gravel"),
                a.BlockAt(sample, x, z, 4, deep));
            Assert.Equal(blocks.GetId("asteria:stone"),
                a.BlockAt(sample, x, z, 6, deep));
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
        Assert.Equal(4, variants.Count);
        Assert.All(variants, structure =>
        {
            Assert.False(structure.Locatable);
            Assert.False(structure.Restrictions.RequiresDryGround);
            Assert.Equal(1f, structure.Restrictions.RequiredBiomeCoverage);
            Assert.Equal(StructureFluidPolicy.Displace, structure.Generation.FluidPolicy);
            Assert.Equal(StructureReplacePolicy.Terrain, structure.Generation.ReplacePolicy);
            Assert.Contains("asteria:sand", structure.Restrictions.GroundBlocks);
            Assert.Contains("asteria:clay", structure.Restrictions.GroundBlocks);
            Assert.Contains(structure.Voxels, voxel =>
                voxel.Block == "asteria:stone_cobble");
            Assert.All(structure.Voxels, voxel =>
                Assert.Contains(voxel.Block, new[] {
                    "asteria:stone", "asteria:stone_cobble",
                    "asteria:clay", "asteria:gravel",
                }));
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
        Assert.True(oceanRoots.Length >= 4);
        Assert.Contains(oceanRoots, root =>
            root.Structure == "asteria:coral_fan_garden");
        Assert.Contains(oceanRoots, root =>
            root.Structure == "asteria:ocean_kelp_grove");
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
        Assert.Equal(6, cave.Decorations.Count);
        Assert.Equal(7, floating.Decorations.Count);
        Assert.Contains(floating.Decorations, d => d.Block == "asteria:sky_vines");

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
        var aetherCrystal = blocks.GetId("asteria:aether_crystal");
        var skyVines = blocks.GetId("asteria:sky_vines");
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
            // Stone is now intentionally valid for the authored crystal
            // and ceiling-vine decorators, but not for vegetation.
            var onStone = field.BlockAt(islandSample, stone, x, z,
                new SurfacePlacementContext(240, 0), verticalY:240);
            Assert.True(onStone.IsAir ||
                onStone == aetherCrystal || onStone == skyVines);
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

    [Fact]
    public void DirectionalPaletteFallsBackAndUsesNormalRelativeDepth()
    {
        var blocks = BlockRegistry.FromJson(ReadFiles("blocks"));
        var stone = new BiomeSurfaceLayerDefinition("asteria:stone");
        var cave = new BiomeDefinition(
            "asteria:test/caverns", null, null,
            volumeLayout: new BiomeVolumeLayoutDefinition(
                placement: VolumeBiomePlacement.CarvedVoid),
            palette: new BiomePaletteDefinition(
                [stone],
                floor: [
                    new BiomeSurfaceLayerDefinition("asteria:gravel", 2),
                    stone,
                ],
                walls: [
                    new BiomeSurfaceLayerDefinition("asteria:basalt", 3),
                    stone,
                ],
                ceiling: [
                    new BiomeSurfaceLayerDefinition("asteria:ice", 1),
                    stone,
                ]));
        var palette = new BiomeSurfaceMaterialField(1234UL, [cave], blocks);
        var biome = new BiomeSample(cave.Id,
            [new BiomeInfluence(cave.Id, 1f)]);
        Assert.Equal(blocks.GetId("asteria:gravel"),
            palette.VolumeBlockAt(biome, 2, 40, 6, BiomePaletteFace.Floor, 0));
        Assert.Equal(blocks.GetId("asteria:gravel"),
            palette.VolumeBlockAt(biome, 2, 39, 6, BiomePaletteFace.Floor, 1));
        Assert.Equal(blocks.GetId("asteria:stone"),
            palette.VolumeBlockAt(biome, 2, 38, 6, BiomePaletteFace.Floor, 2));
        for (uint depth = 0; depth < 3; depth++)
            Assert.Equal(blocks.GetId("asteria:basalt"),
                palette.VolumeBlockAt(biome, 2 + (int)depth, 40, 6,
                    BiomePaletteFace.Walls, depth));
        Assert.Equal(blocks.GetId("asteria:stone"),
            palette.VolumeBlockAt(biome, 5, 40, 6, BiomePaletteFace.Walls, 3));
        Assert.Equal(blocks.GetId("asteria:ice"),
            palette.VolumeBlockAt(biome, 2, 40, 6, BiomePaletteFace.Ceiling, 0));
        Assert.Equal(blocks.GetId("asteria:stone"),
            palette.VolumeBlockAt(biome, 2, 41, 6, BiomePaletteFace.Ceiling, 1));
        Assert.Equal(3u, palette.MaxVolumePaintDepth(biome));
        Assert.True(palette.HasDirectionalOverride(biome, BiomePaletteFace.Walls));
        Assert.Equal("asteria:gravel", cave.SurfaceLayers[0].Block);
    }

    [Fact]
    public void PaletteJsonSupportsDirectionalProfilesAndRejectsObsoleteLayers()
    {
        var biome = BiomeDefinitionJson.Parse(
            """
            {
              "id":"asteria:test/caverns",
              "volumeLayout":{"placement":"carvedVoid"},
              "palette":{
                "default":[{"block":"asteria:stone"}],
                "walls":[
                  {"block":"asteria:basalt","depth":3},
                  {"block":"asteria:stone"}
                ]
              }
            }
            """);
        Assert.Equal("asteria:stone", biome.Palette.For(BiomePaletteFace.Floor)[0].Block);
        Assert.Equal("asteria:stone", biome.Palette.For(BiomePaletteFace.Ceiling)[0].Block);
        Assert.Equal("asteria:basalt", biome.Palette.For(BiomePaletteFace.Walls)[0].Block);
        Assert.False(biome.Palette.HasOverride(BiomePaletteFace.Floor));
        Assert.True(biome.Palette.HasOverride(BiomePaletteFace.Walls));

        Assert.Throws<FormatException>(() => BiomeDefinitionJson.Parse(
            """
            {
              "id":"asteria:test/legacy",
              "volumeLayout":{"placement":"carvedVoid"},
              "surfaceLayers":[{"block":"asteria:stone"}]
            }
            """));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomePaletteDefinition(
                [new BiomeSurfaceLayerDefinition("asteria:stone")],
                walls: [new BiomeSurfaceLayerDefinition("asteria:basalt", 3)]));
    }

    private static IEnumerable<string> ReadFiles(string folder) =>
        Directory.EnumerateFiles(
            Path.Combine(PackData, folder), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
