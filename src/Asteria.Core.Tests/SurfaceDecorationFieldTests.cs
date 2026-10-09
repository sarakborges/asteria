using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SurfaceDecorationFieldTests
{
    [Fact]
    public void ClusteredDecorationsAreDeterministicAndStayOnAllowedSurfaces()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:grass_block"),
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:pebble"),
        ]);
        var biome = new BiomeDefinition(
            "asteria:test/plains",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0, 0, 64, 0, 32),
            [new BiomeSurfaceLayerDefinition("asteria:grass_block")],
            decorations:
            [
                new BiomeDecorationDefinition(
                    "asteria:pebble", 1f,
                    ["asteria:grass_block"],
                    new BiomeDecorationClusterDefinition(12, 0f)),
            ]);
        var field = new SurfaceDecorationField(71, [biome], blocks);
        var another = new SurfaceDecorationField(71, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var pebble = blocks.GetId("asteria:pebble");
        var grass = blocks.GetId("asteria:grass_block");
        var stone = blocks.GetId("asteria:stone");
        var occupied = 0;
        var empty = 0;
        for (var z = -32; z <= 32; z++)
        {
            for (var x = -32; x <= 32; x++)
            {
                var value = field.BlockAt(sample, grass, x, z);
                Assert.Equal(value, another.BlockAt(sample, grass, x, z));
                Assert.True(field.BlockAt(sample, stone, x, z).IsAir);
                if (value == pebble)
                {
                    occupied++;
                }
                else
                {
                    Assert.True(value.IsAir);
                    empty++;
                }
            }
        }

        Assert.True(occupied > 0);
        Assert.True(empty > 0);
    }

    [Fact]
    public void GroundObjectsUseIndependentDeterministicChances()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:grass_block"),
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:pebble"),
            new BlockDefinition("asteria:stick"),
        ]);
        var biome = new BiomeDefinition(
            "asteria:test/plains",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0, 0, 64, 0, 32),
            [new BiomeSurfaceLayerDefinition("asteria:grass_block")],
            decorations:
            [
                new BiomeDecorationDefinition(
                    "asteria:pebble", 0.3f, ["asteria:grass_block"]),
                new BiomeDecorationDefinition(
                    "asteria:stick", 0.5f, ["asteria:grass_block"],
                    new BiomeDecorationClusterDefinition(18, -0.5f)),
            ]);
        var field = new SurfaceDecorationField(71, [biome], blocks);
        var sameSeed = new SurfaceDecorationField(71, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var observed = new HashSet<BlockRuntimeId>();
        var grass = blocks.GetId("asteria:grass_block");
        for (var z = -32; z <= 32; z++)
        {
            for (var x = -32; x <= 32; x++)
            {
                var actual = field.BlockAt(sample, grass, x, z);
                Assert.Equal(actual, sameSeed.BlockAt(sample, grass, x, z));
                observed.Add(actual);
                Assert.True(field.BlockAt(
                    sample, blocks.GetId("asteria:stone"), x, z).IsAir);
            }
        }
        Assert.Contains(blocks.GetId("asteria:stick"), observed);
        Assert.Contains(blocks.GetId("asteria:pebble"), observed);
        Assert.Contains(BlockRuntimeId.Air, observed);
    }

    [Fact]
    public void MaterialPatchesRespectAuthoredAltitudeAndSlope()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:grass_block"),
            new BlockDefinition("asteria:mud"),
            new BlockDefinition("asteria:stone"),
        ]);
        var biome = new BiomeDefinition(
            "asteria:test/conditional",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0, 0, 64, 0, 32),
            [
                new BiomeSurfaceLayerDefinition(
                    "asteria:grass_block",
                    1,
                    new BiomeSurfacePatchDefinition(
                        24, 1f, 0f,
                        ["asteria:mud"],
                        new SurfacePlacementConditions(
                            minY: 90, maxSlope: 2))),
                new BiomeSurfaceLayerDefinition("asteria:stone"),
            ]);
        var field = new BiomeSurfaceMaterialField(7, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var grass = blocks.GetId("asteria:grass_block");
        var mud = blocks.GetId("asteria:mud");

        Assert.Equal(mud, field.BlockAt(
            sample, 0, 0, 0, new SurfacePlacementContext(90, 1)));
        Assert.Equal(grass, field.BlockAt(
            sample, 0, 0, 0, new SurfacePlacementContext(89, 1)));
        Assert.Equal(grass, field.BlockAt(
            sample, 0, 0, 0, new SurfacePlacementContext(100, 3)));
        Assert.Equal(mud, field.SampleColumn(
            sample, 0, 0, new SurfacePlacementContext(95, 1))
            .BlockAt(0));
        Assert.Equal(grass, field.SampleColumn(
            sample, 0, 0, new SurfacePlacementContext(95, 4))
            .BlockAt(0));
    }

    [Fact]
    public void DecoratorConditionsFilterAltitudeAndSlopeBeforeChance()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:grass_block"),
            new BlockDefinition("asteria:pebble"),
        ]);
        var biome = new BiomeDefinition(
            "asteria:test/conditional",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0, 0, 64, 0, 32),
            [new BiomeSurfaceLayerDefinition("asteria:grass_block")],
            decorations:
            [
                new BiomeDecorationDefinition(
                    "asteria:pebble", 1f, ["asteria:grass_block"],
                    conditions: new SurfacePlacementConditions(
                        minY: 90, maxY: 105, minSlope: 2, maxSlope: 8)),
            ]);
        var field = new SurfaceDecorationField(7, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        var grass = blocks.GetId("asteria:grass_block");
        var pebble = blocks.GetId("asteria:pebble");

        Assert.Equal(pebble, field.BlockAt(
            sample, grass, 0, 0, new SurfacePlacementContext(94, 4)));
        Assert.True(field.BlockAt(
            sample, grass, 0, 0, new SurfacePlacementContext(89, 4)).IsAir);
        Assert.True(field.BlockAt(
            sample, grass, 0, 0, new SurfacePlacementContext(94, 0)).IsAir);
        Assert.True(field.BlockAt(
            sample, grass, 0, 0, new SurfacePlacementContext(106, 4)).IsAir);
    }

    [Fact]
    public void ConditionalPatchAndClusterDataParseWithoutIdSpecificLogic()
    {
        var biome = BiomeDefinitionJson.Parse(
            """
            {
              "id":"asteria:test/conditional",
              "surfaceLayout":{},
              "surfaceTerrain":{
                "baseHeightOffset":0,"macroAmplitude":0,
                "macroScale":64,"detailAmplitude":0,"detailScale":32
              },
              "palette":{"default":[
                {"block":"asteria:grass_block","depth":1,
                 "patch":{"scale":24,"coverage":1,"roughness":0,
                          "blocks":["asteria:mud"],
                          "conditions":{"minY":90,"maxSlope":2}}},
                {"block":"asteria:stone"}
              ]},
              "decorations":[
                {"block":"asteria:pebble","chance":0.4,
                 "surfaceBlocks":["asteria:grass_block"],
                 "conditions":{"minSlope":1},
                 "cluster":{"scale":16,"threshold":0,
                            "octaves":4,"transitionWidth":0.2}}
              ]
            }
            """);

        Assert.Equal(90, biome.SurfaceLayers[0].Patch!.Conditions!.MinY);
        Assert.Equal(2d, biome.SurfaceLayers[0].Patch!.Conditions!.MaxSlope);
        var decorator = Assert.Single(biome.Decorations);
        Assert.Equal(1d, decorator.Conditions!.MinSlope);
        Assert.Equal(4, decorator.Cluster!.Octaves);
        Assert.Equal(0.2f, decorator.Cluster.TransitionWidth);
    }

    [Fact]
    public void InvalidPlacementBoundsAndClusterTransitionsAreRejected()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new SurfacePlacementConditions(minY: -1));
        Assert.ThrowsAny<ArgumentException>(() =>
            new SurfacePlacementConditions(minY: 100, maxY: 90));
        Assert.ThrowsAny<ArgumentException>(() =>
            new SurfacePlacementConditions(minSlope: 5, maxSlope: 4));
        Assert.ThrowsAny<ArgumentException>(() =>
            new SurfacePlacementConditions(maxSlope: double.NaN));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeDecorationClusterDefinition(16, 0f, octaves: 0));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeDecorationClusterDefinition(
                16, 0f, transitionWidth: float.NaN));
    }

    [Fact]
    public void ClusterParametersRejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BiomeDecorationClusterDefinition(1, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BiomeDecorationClusterDefinition(16, float.NaN));
    }
}
