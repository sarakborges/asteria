using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SurfaceMaterialColumnTests
{
    private const ulong Seed = 0x51A7_E2A1_2026UL;

    [Fact]
    public void SampledColumnMatchesAuthoredDepthBoundariesAndCoreMaterial()
    {
        var biome = BiomeDefinitionJson.Parse(
            """
            {
              "id": "asteria:test/sampled_material_column",
              "surfaceLayout": {
                "regionSize": { "min": 128, "max": 128 }
              },
              "surfaceTerrain": {
                "baseHeightOffset": 40,
                "macroAmplitude": 0,
                "macroScale": 64,
                "detailAmplitude": 0,
                "detailScale": 32
              },
              "palette": {
                "default": [
                  { "block": "asteria:grass", "depth": 1 },
                  { "block": "asteria:dirt", "depth": 3 },
                  { "block": "asteria:clay", "depth": 2 },
                  { "block": "asteria:stone" }
                ]
              }
            }
            """);
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:grass"),
            new BlockDefinition("asteria:dirt"),
            new BlockDefinition("asteria:clay"),
            new BlockDefinition("asteria:stone"),
        ]);
        var materials = new BiomeSurfaceMaterialField(Seed, [biome], blocks);
        var sample = new BiomeSample(
            biome.Id, [new BiomeInfluence(biome.Id, 1f)]);
        const int worldX = 137;
        const int worldZ = -59;

        var column = materials.SampleColumn(sample, worldX, worldZ);

        Assert.Equal(6u, column.FiniteDepth);
        for (uint depth = 0; depth < 14; depth++)
        {
            Assert.Equal(
                materials.BlockAt(sample, worldX, worldZ, depth),
                column.BlockAt(depth));
        }

        Assert.Equal(blocks.GetId("asteria:grass"), column.BlockAt(0));
        Assert.Equal(blocks.GetId("asteria:dirt"), column.BlockAt(1));
        Assert.Equal(blocks.GetId("asteria:dirt"), column.BlockAt(3));
        Assert.Equal(blocks.GetId("asteria:clay"), column.BlockAt(4));
        Assert.Equal(blocks.GetId("asteria:stone"), column.BlockAt(6));
        Assert.Equal(blocks.GetId("asteria:stone"), column.BlockAt(uint.MaxValue));
    }
}
