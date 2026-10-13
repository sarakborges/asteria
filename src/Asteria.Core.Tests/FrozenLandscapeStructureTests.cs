using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FrozenLandscapeStructureTests
{
    private static readonly (string Biome, string Family, int Variants,
        string Ground, string Favored, string Disfavored)[] Authored =
    [
        ("arctic", "tree_pine_arctic_stunted", 3, "snow", "frozen_plain", "ice_fields"),
        ("arctic", "brush_frost_arctic", 3, "snow", "frozen_plain", "ice_fields"),
        ("arctic", "spire_rime_arctic", 3, "ice", "ice_fields", "frozen_plain"),
        ("alps", "tree_fir_alpine_snow", 4, "snow", "snowfields", "glacial_ice"),
        ("alps", "tree_krummholz_alpine", 3, "stone", "wind_scoured_rock", "glacial_ice"),
        ("alps", "serac_alpine", 3, "ice", "glacial_ice", "snowfields"),
        ("alps", "arch_ice_alpine", 2, "ice", "glacial_ice", "snowfields"),
    ];

    [Fact]
    public void SevenFrozenLandmarkFamiliesAreHabitatWeightedAndPreserveGroundMaterials()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        var biomeRegistry = BiomeRegistry.FromJson(ReadJson("biomes"));
        var dimension = DimensionRegistry.FromJson(ReadJson("dimensions"))
            .Get(DimensionId.Overworld);

        Assert.Equal("asteria:snow",
            biomeRegistry.Get("asteria:overworld/arctic").SurfaceLayers[0].Block);
        Assert.Equal("asteria:snow",
            biomeRegistry.Get("asteria:overworld/alps").SurfaceLayers[0].Block);
        Assert.Equal(21, Authored.Sum(item => item.Variants));

        foreach (var (biome, family, count, ground, favored, disfavored) in Authored)
        {
            var root = Assert.Single(dimension.GeneratedSurfaceStructures,
                rule => rule.Biome == $"asteria:overworld/{biome}" &&
                        rule.Structure == $"asteria:{family}");
            Assert.InRange(root.Spacing, 25, 110);
            Assert.InRange(root.Chance, 0.5f, 1f);
            Assert.NotNull(root.HabitatWeights);
            Assert.True(root.HabitatWeights!.For(favored) >
                        root.HabitatWeights.For(disfavored));

            var members = structures.ResolveReference($"asteria:{family}");
            Assert.Equal(count, members.Count);
            Assert.Equal(count, members.Select(v => v.Id)
                .Distinct(StringComparer.Ordinal).Count());
            Assert.All(members, member =>
            {
                Assert.True(member.Voxels.Count > 10);
                Assert.True(member.MaximumY >= 4);
                Assert.True(member.Rotation);
                Assert.True(member.Restrictions.RequiresDryGround);
                Assert.InRange(member.Restrictions.MaxSlope, 1, 3);
                Assert.Equal(1f, member.Restrictions.RequiredBiomeCoverage);
                Assert.Equal($"asteria:{ground}",
                    Assert.Single(member.Restrictions.GroundBlocks));
                Assert.Equal(StructureFluidPolicy.Forbid,
                    member.Generation.FluidPolicy);
                Assert.Equal(StructureReplacePolicy.Terrain,
                    member.Generation.ReplacePolicy);

                var groundVoxels = member.Voxels
                    .Where(v => v.Y == 0).ToDictionary(
                        v => (v.X, v.Z), v => v.Block);
                Assert.NotEmpty(groundVoxels);
                Assert.All(groundVoxels.Values,
                    block => Assert.Equal($"asteria:{ground}", block));
                // Every root/pillar at Y=1 must sit on matching ground.
                var rigidRoots = new[]
                {
                    "asteria:log_frost_wood", "asteria:log_rime_deadwood",
                    "asteria:ice"
                };
                foreach (var voxel in member.Voxels.Where(v =>
                             v.Y == 1 && rigidRoots.Contains(v.Block)))
                    Assert.True(groundVoxels.ContainsKey((voxel.X, voxel.Z)),
                        $"{member.Id} has an unsupported base at X={voxel.X}, Z={voxel.Z}");
            });
        }
    }

    [Fact]
    public void FrozenLandmarksActuallyVaryInHeightAndSilhouette()
    {
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        var spruce = structures.ResolveReference("asteria:tree_fir_alpine_snow");
        Assert.Equal(4, spruce.Count);
        Assert.True(spruce.Max(v => v.MaximumY) >= 14);
        Assert.True(spruce.Min(v => v.MaximumY) <= 9);
        Assert.True(spruce.Max(v => v.MaximumY) -
                    spruce.Min(v => v.MaximumY) >= 4);

        foreach (var group in new[]
        {
            "asteria:tree_pine_arctic_stunted",
            "asteria:spire_rime_arctic",
            "asteria:serac_alpine",
            "asteria:arch_ice_alpine",
        })
        {
            var variants = structures.ResolveReference(group);
            Assert.True(variants.Select(v => v.MaximumY).Distinct().Count() > 1);
        }
        Assert.All(structures.ResolveReference("asteria:arch_ice_alpine"),
            arch => Assert.Contains(arch.Voxels, v =>
                v.Block == "asteria:ice" && v.Y > 4));
    }

    [Fact]
    public void FrozenPlantsHaveCanonicalSnowyTextures()
    {
        var registry = BlockRegistry.FromJson(ReadJson("blocks"));
        foreach (var id in new[]
        {
            "asteria:leaf_frost_wood_arctic", "asteria:leaf_frost_wood_alpine",
        })
        {
            var block = registry.GetDefinition(registry.GetId(id));
            Assert.Equal("foliage", block.Category);
            Assert.True(block.WindSway);
            Assert.Equal(BlockRenderMode.Cutout, block.RenderMode);
            Assert.False(block.CastsShadow);
            Assert.All(block.Textures.AllLayers(),
                layer => Assert.StartsWith("textures/blocks/", layer.Texture));
        }
        var wood = registry.GetDefinition(registry.GetId("asteria:log_frost_wood"));
        Assert.Equal("blocks_wooden", wood.Category);
        Assert.Equal(
            registry.GetDefinition(registry.GetId("asteria:leaf_frost_wood_arctic"))
                .Textures.ForFace(BlockFace.Top).Single().Texture,
            registry.GetDefinition(registry.GetId("asteria:leaf_frost_wood_alpine"))
                .Textures.ForFace(BlockFace.Top).Single().Texture);
    }

    private static IEnumerable<string> ReadJson(string directory)
    {
        var path = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", directory);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
