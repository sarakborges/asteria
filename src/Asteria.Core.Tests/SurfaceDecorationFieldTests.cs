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
    public void ClusterParametersRejectInvalidValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BiomeDecorationClusterDefinition(1, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new BiomeDecorationClusterDefinition(16, float.NaN));
    }
}
