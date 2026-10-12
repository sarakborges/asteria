using Asteria.Core.Content;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlainsSwampBiodiversityTests
{
    private static readonly string[] PlainsFlowers =
    [
        "daisy", "clover",
        "purple_aster", "meadow_seedheads"
    ];

    [Fact]
    public void PlainsMeadowObjectsStayInGrassAndPreserveOpenHabitats()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var plains = biomes.Get("asteria:overworld/plains");
        Assert.Equal(10, plains.Decorations.Count);
        Assert.Equal("asteria:grass_block", plains.SurfaceLayers[0].Block);
        foreach (var name in PlainsFlowers)
        {
            var rule = Assert.Single(plains.Decorations,
                x => x.Block == "asteria:" + name);
            Assert.Equal(["asteria:grass_block"], rule.SurfaceBlocks);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.HabitatWeights);
            Assert.NotNull(rule.Conditions);
            Assert.True(rule.HabitatWeights!.For("open") >
                        rule.HabitatWeights.For("rocky"));
            Assert.Equal(DecorationFluidPlacement.Dry, rule.FluidPlacement);
            if (name is "clover" or "daisy" or "purple_aster")
            {
                Assert.True(rule.IsObject);
                Assert.False(blocks.TryGetId(rule.Block, out _));
                var objects = PackContentRegistry<ObjectDefinition>.FromJson(
                    ReadPackJson("objects"), ObjectDefinition.Parse, d => d.Id);
                var visual = objects.Get(rule.Block).Visual;
                Assert.Equal(name == "clover"
                    ? ObjectVisualKind.ExtrudedSprite
                    : ObjectVisualKind.CrossedSprite, visual.Kind);
                Assert.Equal("textures/objects/" + name + ".png", visual.Texture);
            }
            else
            {
                var block = blocks.GetDefinition(blocks.GetId(rule.Block));
                Assert.True(block.HasTag(BlockPhysicsCapabilities.SupportBelow));
                Assert.False(block.IsCollidable);
                Assert.Equal(BlockRenderMode.Cutout, block.RenderMode);
                Assert.Equal(BlockVisualKind.CrossedSprite, block.Visual.Kind);
                Assert.Equal("textures/objects/" + name + ".png",
                    block.Visual.Texture!.Value.Texture);
            }
        }
    }

    [Fact]
    public void SwampOrchidsAndBulrushesRequireWaterNearbyOnDryGround()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var biomes = BiomeRegistry.FromJson(ReadPackJson("biomes"));
        biomes.ValidateBlocks(blocks);
        var swamp = biomes.Get("asteria:overworld/swamp");
        Assert.Equal(11, swamp.Decorations.Count);
        Assert.Equal("asteria:mud", swamp.SurfaceLayers[0].Block);
        foreach (var name in new[] { "marsh_orchid", "bulrush" })
        {
            var rule = Assert.Single(swamp.Decorations,
                x => x.Block == "asteria:" + name);
            Assert.NotNull(rule.FluidRequirement);
            Assert.Equal("asteria:water", rule.FluidRequirement!.Fluid);
            Assert.Equal(DecorationFluidRelation.Nearby,
                rule.FluidRequirement.Relation);
            Assert.NotNull(rule.Cluster);
            Assert.NotNull(rule.Conditions);
            Assert.True(rule.HabitatWeights!.For("open_mire") >
                        rule.HabitatWeights.For("fungal_ground"));
            Assert.All(rule.SurfaceBlocks, b => Assert.Contains(b,
                new[] {"asteria:mud", "asteria:dirt", "asteria:grass_block"}));
            Assert.True(rule.IsObject);
            Assert.False(blocks.TryGetId(rule.Block, out _));
            var objects = PackContentRegistry<ObjectDefinition>.FromJson(
                ReadPackJson("objects"), ObjectDefinition.Parse, d => d.Id);
            var definition = objects.Get(rule.Block);
            Assert.True(definition.Supports(BlockFace.Top));
            Assert.Equal(ObjectVisualKind.CrossedSprite, definition.Visual.Kind);
        }
    }

    [Fact]
    public void MarshBedAndMossStumpVariantsAreGroundedAndRemainBiomelimited()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        structures.ValidateBlocks(blocks);
        var dim = DimensionRegistry.FromJson(ReadPackJson("dimensions"))
            .Get(DimensionId.Overworld);
        foreach (var name in new[] { "swamp_reed_bed", "swamp_moss_stump" })
        {
            var rule = Assert.Single(dim.GeneratedSurfaceStructures,
                x => x.Biome == "asteria:overworld/swamp" &&
                     x.Structure == "asteria:" + name);
            Assert.NotNull(rule.HabitatWeights);
            var members = structures.ResolveReference("asteria:" + name);
            Assert.Equal(3, members.Count);
            Assert.All(members, s =>
            {
                Assert.Equal((int?)0, s.GroundAnchorY);
                Assert.Equal(1f, s.Restrictions.RequiredBiomeCoverage);
                Assert.True(s.Restrictions.RequiresDryGround);
                Assert.Equal(StructureFluidPolicy.Forbid,
                    s.Generation.FluidPolicy);
                Assert.All(s.Restrictions.GroundBlocks, ground =>
                    Assert.Contains(ground, new[]
                    {
                        "asteria:mud","asteria:dirt","asteria:grass_block"
                    }));
                if (name == "swamp_reed_bed")
                {
                    Assert.True(s.Objects.Count >= 8);
                    Assert.Empty(s.Voxels);
                    Assert.All(s.Objects, obj =>
                    {
                        Assert.Equal(1, obj.Y);
                        Assert.Equal(BlockFace.Top, obj.Face);
                        Assert.Contains(obj.Object,
                            new[] { "asteria:bulrush", "asteria:cattail" });
                    });
                }
                else
                {
                    Assert.True(s.Voxels.Count >= 8);
                    Assert.All(s.Voxels, v => Assert.True(v.Y > 0));
                }
            });
        }

        var reedBeds = structures.ResolveReference("asteria:swamp_reed_bed");
        Assert.All(reedBeds, bed =>
        {
            Assert.All(bed.Objects, obj => Assert.Equal(1, obj.Y));
            Assert.Contains(bed.Restrictions.Proximity, p =>
                p.Target.Fluid == "asteria:water" &&
                p.Mode == StructureProximityMode.Required);
        });
        var stumps = structures.ResolveReference("asteria:swamp_moss_stump");
        Assert.True(stumps.Select(x => x.MaximumY).Distinct().Count() >= 2);
        Assert.All(stumps, stump =>
        {
            Assert.Contains(stump.Voxels, v =>
                v.Block == "asteria:log_willow");
            Assert.Contains(stump.Voxels, v =>
                v.Block == "asteria:shelf_fungus");
            Assert.Contains(stump.Voxels, v =>
                v.Block == "asteria:hanging_moss");
        });
    }

    [Fact]
    public void HangingMossOnStumpsIsActuallySupportedByWillowWood()
    {
        var structures = StructureRegistry.FromJson(ReadPackJson("structures"));
        var moss = structures.ResolveReference("asteria:swamp_moss_stump");
        Assert.Equal(3, moss.Count);
        foreach (var stump in moss)
        {
            var hangers = stump.Voxels.Where(v =>
                v.Block == "asteria:hanging_moss").ToArray();
            Assert.NotEmpty(hangers);
            foreach (var hanger in hangers)
            {
                Assert.Contains(stump.Voxels, support =>
                    support.X == hanger.X &&
                    support.Z == hanger.Z &&
                    support.Y == hanger.Y + 1 &&
                    (support.Block == "asteria:log_willow"));
            }
        }
    }

    [Fact]
    public void AllNewArtAssetsHaveUniquePackPaths()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var names = PlainsFlowers.Concat(new[]
        {
            "marsh_orchid", "bulrush", "shelf_fungus"
        });
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, d => d.Id);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names)
        {
            var path = name is "clover" or "daisy" or "purple_aster" or "marsh_orchid" or "bulrush"
                ? objects.Get("asteria:" + name).Visual.Texture
                : blocks.GetDefinition(blocks.GetId("asteria:" + name))
                    .Visual.Texture!.Value.Texture;
            Assert.Equal("textures/objects/" + name + ".png", path);
            Assert.True(seen.Add(path));
        }
        var log = blocks.GetDefinition(
            blocks.GetId("asteria:log_willow"));
        Assert.Equal("textures/blocks/trees/willow/side.png",
            Assert.Single(log.Textures.ForFace(BlockFace.Left)).Texture);
        Assert.True(seen.Add(
            Assert.Single(log.Textures.ForFace(BlockFace.Left)).Texture));
        Assert.Equal(8, seen.Count);
    }

    [Fact]
    public void NewMeadowFloraSelectionIsSeedDeterministic()
    {
        var blocks = BlockRegistry.FromJson(ReadPackJson("blocks"));
        var plains = BiomeRegistry.FromJson(ReadPackJson("biomes"))
            .Get("asteria:overworld/plains");
        var sample = new BiomeSample(plains.Id,
            [new BiomeInfluence(plains.Id, 1f)]);
        var objects = PackContentRegistry<ObjectDefinition>.FromJson(
            ReadPackJson("objects"), ObjectDefinition.Parse, d => d.Id);
        var first = new SurfaceDecorationField(29801, [plains], blocks,
            objects: objects);
        var again = new SurfaceDecorationField(29801, [plains], blocks,
            objects: objects);
        var grass = blocks.GetId("asteria:grass_block");
        var basalt = blocks.GetId("asteria:basalt");
        var valid = new SurfacePlacementContext(94, 0);
        for (var z = -24; z <= 24; z++)
        for (var x = -24; x <= 24; x++)
        {
            Assert.Equal(first.BlockAt(sample, grass, x, z, valid),
                         again.BlockAt(sample, grass, x, z, valid));
            Assert.Equal(first.ObjectAt(sample, grass, x, z, valid),
                         again.ObjectAt(sample, grass, x, z, valid));
            Assert.True(first.BlockAt(sample, basalt, x, z, valid).IsAir);
            Assert.Null(first.ObjectAt(sample, basalt, x, z, valid));
        }
    }

    private static IEnumerable<string> ReadPackJson(string dir)
    {
        var folder = Path.Combine(AppContext.BaseDirectory,
            "packs", "default", "data", dir);
        return Directory.EnumerateFiles(folder, "*.json")
            .OrderBy(file => file, StringComparer.Ordinal)
            .Select(File.ReadAllText);
    }
}
