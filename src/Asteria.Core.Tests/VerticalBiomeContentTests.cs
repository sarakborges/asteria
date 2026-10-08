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
        Assert.Null(cave.VolumeLayout);
        Assert.NotNull(cave.UndergroundLayout);
        Assert.Null(floating.SurfaceLayout);
        Assert.Null(floating.UndergroundLayout);
        Assert.NotNull(floating.VolumeLayout);
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

    private static IEnumerable<string> ReadFiles(string folder) =>
        Directory.EnumerateFiles(
            Path.Combine(PackData, folder), "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
}
