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
        Assert.Equal(2, islands.VolumeStructures.Count);
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
    public void AllCaveAndSkyLandmarksUseOwnBiomeAndColoredLight()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        foreach (var (group, count, block) in new[]
        {
            ("cavern_glow_mushroom", 3, "cave_glow_mushroom_cap"),
            ("cavern_crystal_cluster", 2, "cave_crystal_core"),
            ("cavern_chandelier", 2, "cave_crystal_core"),
            ("skywood_tree", 3, "sky_canopy_leaf"),
            ("sky_crystal_arch", 2, "sky_crystal_core"),
        })
        {
            var variants = structures.ResolveReference("asteria:" + group);
            Assert.Equal(count, variants.Count);
            Assert.All(variants, v => Assert.Contains(v.Voxels,
                voxel => voxel.Block == $"asteria:{block}"));
        }
        foreach (var name in new[]
        {
            "cave_glow_mushroom_cap", "cave_crystal_core",
            "sky_crystal_core",
        })
        {
            var definition = blocks.GetDefinition(
                blocks.GetId("asteria:" + name));
            Assert.True(definition.LightEmission.Blue > 0);
            Assert.True(definition.IsCollidable);
        }

        var canopy = blocks.GetDefinition(
            blocks.GetId("asteria:sky_canopy_leaf"));
        Assert.True(canopy.WindSway);
        Assert.Equal(BlockRenderMode.Cutout, canopy.RenderMode);
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

    private static IEnumerable<string> ReadJson(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
