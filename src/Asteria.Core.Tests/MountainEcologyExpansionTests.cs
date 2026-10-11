using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class MountainEcologyExpansionTests
{
    private static readonly (string Biome, string[] Names)[] SmallFlora =
    [
        ("mountains", [
            "stonecrop", "saxifrage", "rock_lichen"
        ]),
        ("mountain_belt", [
            "pass_grass", "thorn_rosette", "scree_lichen"
        ]),
    ];

    private static readonly (string Biome, string Family, int Count,
        string Preferred, string Avoided)[] Landmarks =
    [
        ("mountains", "mountain_dwarf_juniper", 3,
            "foothill_shelves", "exposed_ridges"),
        ("mountains", "mountain_wind_pine", 3,
            "foothill_shelves", "exposed_ridges"),
        ("mountain_belt", "belt_windswept_pine", 3,
            "sheltered_passes", "bare_crests"),
        ("mountain_belt", "belt_rock_needle_cluster", 3,
            "bare_crests", "sheltered_passes"),
    ];

    [Fact]
    public void SmallFloraIsBiomeSpecificAndRootedInExistingMountainMaterials()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        foreach (var (biomeName, names) in SmallFlora)
        {
            var biome = biomes.Get("asteria:overworld/" + biomeName);
            Assert.NotNull(biome.SurfaceHabitats);
            Assert.Equal("asteria:stone", biome.SurfaceLayers[0].Block);
            Assert.Equal(6, biome.Decorations.Count);
            foreach (var name in names)
            {
                var rule = Assert.Single(biome.Decorations,
                    r => r.Block == "asteria:" + name);
                Assert.NotNull(rule.HabitatWeights);
                Assert.NotNull(rule.Conditions);
                Assert.NotNull(rule.Cluster);
                Assert.True(rule.Conditions!.MaxSlope.HasValue);
                Assert.True(rule.Conditions.MaxY.HasValue);
                Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
                Assert.Equal(DecorationSupportSurface.Floor, rule.SupportSurface);
                Assert.All(rule.SurfaceBlocks, ground =>
                    Assert.Contains(ground, new[]
                    {
                        "asteria:stone", "asteria:stone_cobble",
                        "asteria:gravel", "asteria:dirt"
                    }));
                var block = blocks.GetDefinition(blocks.GetId(rule.Block));
                Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
                Assert.False(block.IsCollidable);
                Assert.Equal(BlockRenderMode.Cutout, block.RenderMode);
                Assert.Equal(
                    name.EndsWith("_lichen", StringComparison.Ordinal)
                        ? BlockVisualKind.GroundSprite : BlockVisualKind.CrossedSprite,
                    block.Visual.Kind);
                Assert.Equal("textures/objects/" + name + ".png",
                    block.Visual.Texture!.Value.Texture);
            }
        }
        Assert.DoesNotContain(biomes.Get("asteria:overworld/mountains")
            .Decorations, r => r.Block.StartsWith("asteria:belt_", StringComparison.Ordinal));
        Assert.DoesNotContain(biomes.Get("asteria:overworld/mountain_belt")
            .Decorations, r => r.Block.StartsWith("asteria:mountain_", StringComparison.Ordinal));
    }

    [Fact]
    public void MountainAndBeltTreesHaveDistinctPalettesAndGroundCoverage()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        structures.ValidateBlocks(blocks);
        var dimension = DimensionRegistry.FromJson(ReadPackJson("dimensions"))
            .Get(DimensionId.Overworld);
        foreach (var (biome, family, expected, preferred, avoided) in Landmarks)
        {
            var rule = Assert.Single(dimension.GeneratedSurfaceStructures,
                r => r.Biome == "asteria:overworld/" + biome &&
                     r.Structure == "asteria:" + family);
            Assert.NotNull(rule.HabitatWeights);
            Assert.True(rule.HabitatWeights!.For(preferred) >
                        rule.HabitatWeights.For(avoided));
            Assert.InRange(rule.Spacing, 40, 120);
            var variants = structures.ResolveReference("asteria:" + family);
            Assert.Equal(expected, variants.Count);
            Assert.True(variants.Select(s => s.MaximumY).Distinct().Count() > 1);
            Assert.All(variants, variant =>
            {
                Assert.True(variant.Rotation);
                Assert.Equal((int?)0, variant.GroundAnchorY);
                Assert.Equal(1f, variant.Restrictions.RequiredBiomeCoverage);
                Assert.True(variant.Restrictions.RequiresDryGround);
                Assert.Equal(StructureFluidPolicy.Forbid, variant.Generation.FluidPolicy);
                Assert.Equal(StructureReplacePolicy.Terrain, variant.Generation.ReplacePolicy);
                Assert.All(variant.Restrictions.GroundBlocks,
                    ground => Assert.Equal("asteria:stone", ground));
                Assert.True(variant.Voxels.Count > 10);
                Assert.All(variant.Voxels, v => Assert.True(v.Y > 0));
            });
        }

        var mountain = structures.ResolveReference("asteria:mountain_wind_pine");
        var belt = structures.ResolveReference("asteria:belt_windswept_pine");
        Assert.All(mountain, s => Assert.Contains(s.Voxels,
            v => v.Block == "asteria:juniper_needles"));
        Assert.All(belt, s => Assert.Contains(s.Voxels,
            v => v.Block == "asteria:windwood_needles"));
        Assert.True(mountain.Max(s => s.MaximumY) >= 11);
        Assert.True(belt.Max(s => s.MaximumY) >= 9);

        var needles = structures.ResolveReference("asteria:belt_rock_needle_cluster");
        Assert.All(needles, s =>
        {
            Assert.All(s.Voxels, v => Assert.Equal("asteria:stone", v.Block));
            Assert.True(s.Voxels.Count > 15);
        });
    }

    [Fact]
    public void DecorativeBlocksUseTenIndependentPackTextures()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (biome, names) in SmallFlora)
            foreach (var name in names)
            {
                var d = blocks.GetDefinition(blocks.GetId("asteria:" + name));
                Assert.True(seen.Add(d.Visual.Texture!.Value.Texture));
            }
        foreach (var id in new[]
        {
            "juniper_wood", "juniper_needles",
            "windwood", "windwood_needles"
        })
        {
            var d = blocks.GetDefinition(blocks.GetId("asteria:" + id));
            var face = Assert.Single(d.Textures.ForFace(BlockFace.Top));
            Assert.Equal(id switch
            {
                "juniper_wood" => "textures/blocks/trees/juniper/wood.png",
                "juniper_needles" => "textures/blocks/trees/juniper/needles.png",
                "windwood" => "textures/blocks/trees/windwood/wood.png",
                "windwood_needles" => "textures/blocks/trees/windwood/needles.png",
                _ => throw new InvalidOperationException("Unexpected tree material")
            }, face.Texture);
            Assert.True(seen.Add(face.Texture));
            if (id.EndsWith("_needles", StringComparison.Ordinal))
                Assert.Equal(BlockTint.None, d.Tint);
        }
        Assert.Equal(10, seen.Count);
    }

    [Fact]
    public void MountainSmallDecoratorsAreSeedDeterministic()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        var rock = blocks.GetId("asteria:stone");
        var basalt = blocks.GetId("asteria:basalt");
        foreach (var biomeName in new[] { "mountains", "mountain_belt" })
        {
            var b = biomes.Get("asteria:overworld/" + biomeName);
            var sample = new BiomeSample(b.Id, [new BiomeInfluence(b.Id, 1f)]);
            var first = new SurfaceDecorationField(9127, [b], blocks);
            var second = new SurfaceDecorationField(9127, [b], blocks);
            var surface = new SurfacePlacementContext(130, 0);
            for (var z = -18; z <= 18; z++)
            for (var x = -18; x <= 18; x++)
            {
                Assert.Equal(first.BlockAt(sample, rock, x, z, surface),
                             second.BlockAt(sample, rock, x, z, surface));
                Assert.True(first.BlockAt(sample, basalt, x, z, surface).IsAir);
            }
        }
    }

    private static IEnumerable<string> ReadPackJson(string folder)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", folder);
        return Directory.EnumerateFiles(path, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
