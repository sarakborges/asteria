using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WastelandContentExpansionTests
{
    [Fact]
    public void WastelandDecorationsUseRetainedMaterialsAndHabitatBands()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadJson("objects"), ObjectDefinition.Parse, item => item.Id);
        var biomes = BiomeRegistry.FromJson(ReadJson("biomes"));
        biomes.ValidateBlocks(blocks);
        biomes.ValidateObjects(objects);
        var wasteland = biomes.Get("asteria:overworld/wasteland");

        Assert.Equal(["barren_flats", "deadwood_pockets", "rocky_scrub"],
            wasteland.SurfaceHabitats!.Bands.Select(band => band.Id).ToArray());
        Assert.Equal("asteria:dirt", wasteland.SurfaceLayers[0].Block);
        Assert.Equal(
            ["asteria:lichen_dry", "asteria:pebble", "asteria:stick"],
            wasteland.Decorations.Select(rule => rule.Block)
                .OrderBy(id => id, StringComparer.Ordinal));

        foreach (var rule in wasteland.Decorations)
        {
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotEmpty(rule.SurfaceBlocks);
            Assert.All(rule.SurfaceBlocks, support => Assert.Contains(support,
                new[] { "asteria:dirt", "asteria:gravel", "asteria:stone" }));
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
            Assert.Equal(DecorationSupportSurface.Floor, rule.SupportSurface);

            if (rule.IsObject)
            {
                var obj = objects.Get(rule.Block);
                Assert.True(obj.Supports(BlockFace.Top));
                Assert.Equal(ObjectVisualKind.ExtrudedSprite, obj.Visual.Kind);
                Assert.True(obj.DropSelf);
                Assert.Contains(obj.Visual.Texture, new[]
                {
                    "textures/items/pebble.png", "textures/items/stick.png"
                });
            }
            else
            {
                Assert.Equal("asteria:lichen_dry", rule.Block);
                var block = blocks.GetDefinition(blocks.GetId(rule.Block));
                Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
                Assert.Equal(BlockVisualKind.GroundSprite, block.Visual.Kind);
                Assert.False(block.IsCollidable);
                Assert.Equal("textures/layers/lichen_dry.png",
                    block.Visual.Texture!.Value.Texture);
            }
        }

        var pebble = Assert.Single(wasteland.Decorations,
            rule => rule.Block == "asteria:pebble");
        Assert.True(pebble.HabitatWeights!.For("rocky_scrub") >
                    pebble.HabitatWeights.For("barren_flats"));
        var stick = Assert.Single(wasteland.Decorations,
            rule => rule.Block == "asteria:stick");
        Assert.True(stick.HabitatWeights!.For("deadwood_pockets") >
                    stick.HabitatWeights.For("barren_flats"));
        var lichen = Assert.Single(wasteland.Decorations,
            rule => rule.Block == "asteria:lichen_dry");
        Assert.True(lichen.HabitatWeights!.For("rocky_scrub") >
                    lichen.HabitatWeights.For("barren_flats"));
    }

    [Fact]
    public void FossilRibsRemainDistinctAndGrounded()
    {
        var blocks = BlockRegistry.FromJson(ReadJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadJson("structures"));
        structures.ValidateBlocks(blocks);
        var dimension = DimensionRegistry.FromJson(ReadJson("dimensions"))
            .Get(DimensionId.Overworld);

        foreach (var (name, variantsExpected, preferred) in new[]
        {
            ("rib_fossil_wasteland", 3, "rocky_scrub"),
        })
        {
            var root = Assert.Single(dimension.GeneratedSurfaceStructures,
                rule => rule.Biome == "asteria:overworld/wasteland" &&
                        rule.Structure == "asteria:" + name);
            Assert.True(root.HabitatWeights!.For(preferred) >
                root.HabitatWeights.For("barren_flats"));
            var members = structures.ResolveReference("asteria:" + name);
            Assert.Equal(variantsExpected, members.Count);
            Assert.True(members.Select(member => member.MaximumY).Distinct().Count() >= 3);
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
                    voxel => voxel.Block == "asteria:bone_fossil" &&
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
    public void DryLichenHasBoundedSteepSlopeRejection()
    {
        var wasteland = BiomeRegistry.FromJson(ReadJson("biomes"))
            .Get("asteria:overworld/wasteland");
        var lichen = Assert.Single(wasteland.Decorations,
            rule => rule.Block == "asteria:lichen_dry");
        Assert.NotNull(lichen.Conditions);
        Assert.Equal(4f, lichen.Conditions!.MaxSlope);
    }

    private static IEnumerable<string> ReadJson(string dir)
    {
        var root = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", dir);
        return Directory.EnumerateFiles(root, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
