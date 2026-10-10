using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WastelandContentExpansionTests
{
    private static readonly (string Name, BlockVisualKind Kind,
        string[] Ground)[] Objects =
    [
        ("wasteland_dead_thistle", BlockVisualKind.CrossedSprite,
            ["asteria:dirt", "asteria:gravel"]),
        ("wasteland_brittle_tuft", BlockVisualKind.CrossedSprite,
            ["asteria:dirt"]),
        ("wasteland_dry_lichen", BlockVisualKind.GroundSprite,
            ["asteria:gravel", "asteria:stone"]),
        ("wasteland_bleached_roots", BlockVisualKind.GroundSprite,
            ["asteria:dirt", "asteria:gravel"]),
        ("wasteland_bone_scatter", BlockVisualKind.GroundSprite,
            ["asteria:dirt", "asteria:gravel", "asteria:stone"]),
    ];

    [Fact]
    public void NewDecorationsRespectActualTerrainMaterialsAndHabitatBands()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var wasteland = biomes.Get("asteria:overworld/wasteland");

        Assert.Equal(["barren_flats", "deadwood_pockets", "rocky_scrub"],
            wasteland.SurfaceHabitats!.Bands.Select(band => band.Id).ToArray());
        Assert.Equal("asteria:dirt", wasteland.SurfaceLayers[0].Block);
        Assert.Equal(7, wasteland.Decorations.Count);

        foreach (var (name, kind, ground) in Objects)
        {
            var rule = Assert.Single(wasteland.Decorations,
                d => d.Block == "asteria:" + name);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.Conditions);
            Assert.NotNull(rule.HabitatWeights);
            Assert.InRange(rule.Chance, 0.04f, 0.16f);
            Assert.Equal(ground, rule.SurfaceBlocks);
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
            Assert.Equal(DecorationSupportSurface.Floor, rule.SupportSurface);
            var block = blocks.GetDefinition(blocks.GetId(rule.Block));
            Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
            Assert.Equal(kind, block.Visual.Kind);
            Assert.False(block.IsCollidable);
            Assert.False(block.CastsShadow);
            Assert.Equal("textures/objects/" + name + ".png",
                block.Visual.Texture!.Value.Texture);
        }

        var thistle = Assert.Single(wasteland.Decorations,
            rule => rule.Block == "asteria:wasteland_dead_thistle");
        Assert.True(thistle.HabitatWeights!.For("rocky_scrub") >
            thistle.HabitatWeights.For("barren_flats"));
        var roots = Assert.Single(wasteland.Decorations,
            rule => rule.Block == "asteria:wasteland_bleached_roots");
        Assert.True(roots.HabitatWeights!.For("deadwood_pockets") >
            roots.HabitatWeights.For("barren_flats"));
    }

    [Fact]
    public void StructuralThornThicketsAndFossilRibsAreDistinctAndGrounded()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        var dimension = DimensionRegistry.FromJson(ReadJson("dimensions"))
            .Get(DimensionId.Overworld);

        foreach (var (name, variantsExpected, preferred) in new[]
        {
            ("wasteland_thorn_scrub", 4, "rocky_scrub"),
            ("wasteland_fossil_rib", 3, "rocky_scrub"),
        })
        {
            var root = Assert.Single(dimension.GeneratedSurfaceStructures,
                rule => rule.Biome == "asteria:overworld/wasteland" &&
                        rule.Structure == "asteria:" + name);
            Assert.True(root.HabitatWeights!.For(preferred) >
                root.HabitatWeights.For("barren_flats"));
            var members = structures.ResolveReference("asteria:" + name);
            Assert.Equal(variantsExpected, members.Count);
            Assert.True(members.Select(m => m.MaximumY).Distinct().Count() >= 3);
            Assert.All(members, structure =>
            {
                Assert.Equal(0, structure.GroundAnchorY);
                Assert.True(structure.Rotation);
                Assert.True(structure.Restrictions.RequiresDryGround);
                Assert.Equal(1f, structure.Restrictions.RequiredBiomeCoverage);
                Assert.Equal(StructureFluidPolicy.Forbid, structure.Generation.FluidPolicy);
                Assert.Equal(StructureReplacePolicy.Terrain, structure.Generation.ReplacePolicy);
                Assert.True(structure.Voxels.Count > 10);
                Assert.All(structure.Voxels,
                    voxel => Assert.True(voxel.Y > 0,
                        "Structures must preserve the underlying material."));
                Assert.Contains(structure.Voxels,
                    voxel => voxel.Block == (name == "wasteland_thorn_scrub"
                        ? "asteria:wasteland_thornwood"
                        : "asteria:wasteland_fossil_bone") &&
                        voxel.X == 0 && voxel.Z == 0 && voxel.Y == 1);
            });
        }
    }

    [Fact]
    public void DecorationSelectionIsDeterministicAndAvoidsForeignMaterials()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var wasteland = BiomeRegistry.FromJson(ReadJson("biomes"))
            .Get("asteria:overworld/wasteland");
        var first = new SurfaceDecorationField(1245, [wasteland], blocks);
        var again = new SurfaceDecorationField(1245, [wasteland], blocks);
        var sample = new BiomeSample(wasteland.Id,
            [new BiomeInfluence(wasteland.Id, 1f)]);
        var valid = new SurfacePlacementContext(92, 0);
        var stone = blocks.GetId("asteria:stone");
        var basalt = blocks.GetId("asteria:basalt");
        var encountered = new HashSet<BlockRuntimeId>();

        for (int z = -32; z <= 32; z++)
        for (int x = -32; x <= 32; x++)
        {
            var ground = first.BlockAt(sample, stone, x, z, valid);
            Assert.Equal(ground, again.BlockAt(sample, stone, x, z, valid));
            Assert.True(first.BlockAt(sample, basalt, x, z, valid).IsAir);
            if (!ground.IsAir) encountered.Add(ground);
        }
        Assert.NotEmpty(encountered);
    }


    [Fact]
    public void NewWastelandDecoratorsHaveBoundedSteepSlopeRejection()
    {
        var wasteland = BiomeRegistry.FromJson(ReadJson("biomes"))
            .Get("asteria:overworld/wasteland");
        foreach (var id in new[]
        {
            "wasteland_dead_thistle", "wasteland_brittle_tuft",
            "wasteland_dry_lichen", "wasteland_bleached_roots",
            "wasteland_bone_scatter",
        })
        {
            var rule = Assert.Single(wasteland.Decorations,
                d => d.Block == "asteria:" + id);
            Assert.NotNull(rule.Conditions!.MaxSlope);
            Assert.InRange(rule.Conditions.MaxSlope!.Value, 2f, 4f);
        }
    }

    private static IEnumerable<string> ReadJson(string dir)
    {
        var root = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", dir);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
