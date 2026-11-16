using Asteria.Core.Diagnostics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BiomeMapDiagnosticTests
{
    [Fact]
    public void PrimaryMapIsDeterministicAndMatchesBiomeSamples()
    {
        var definitions =
            Definitions();
        var dimension =
            Dimension(
                definitions.Select(
                    value =>
                        value.Id));
        var first =
            new BiomeField(
                987UL,
                dimension,
                new BiomeRegistry(
                    definitions));
        var second =
            new BiomeField(
                987UL,
                dimension,
                new BiomeRegistry(
                    definitions.Reverse()));

        var firstMap =
            BiomeMapDiagnostic.Render(
                first,
                -96,
                48,
                32,
                24,
                step: 8,
                BiomeMapMode.Primary);
        var secondMap =
            BiomeMapDiagnostic.Render(
                second,
                -96,
                48,
                32,
                24,
                step: 8,
                BiomeMapMode.Primary);

        Assert.Equal(
            firstMap.Legend,
            secondMap.Legend);
        Assert.Equal(
            firstMap.Pixels,
            secondMap.Pixels);

        var colors =
            firstMap.Legend.ToDictionary(
                entry =>
                    entry.BiomeId,
                entry =>
                    entry.Color,
                StringComparer.Ordinal);

        for (var z = 0;
             z < firstMap.Depth;
             z++)
        {
            for (var x = 0;
                 x < firstMap.Width;
                 x++)
            {
                var worldX =
                    -96 +
                    x *
                    8;
                var worldZ =
                    48 +
                    z *
                    8;
                var sample =
                    first.Sample(
                        worldX,
                        worldZ);

                Assert.Equal(
                    colors[
                        sample.Primary],
                    firstMap[
                        x,
                        z]);
            }
        }
    }

    [Fact]
    public void InfluenceMapKeepsStableLegendAndProducesBlendedPixels()
    {
        var definitions =
            Definitions();
        var field =
            new BiomeField(
                77UL,
                Dimension(
                    definitions.Select(
                        value =>
                            value.Id)),
                new BiomeRegistry(
                    definitions));

        var primary =
            BiomeMapDiagnostic.Render(
                field,
                -1024,
                -1024,
                128,
                128,
                step: 16,
                BiomeMapMode.Primary);
        var influences =
            BiomeMapDiagnostic.Render(
                field,
                -1024,
                -1024,
                128,
                128,
                step: 16,
                BiomeMapMode.Influences);

        Assert.Equal(
            primary.Legend,
            influences.Legend);
        Assert.Contains(
            Enumerable.Range(
                    0,
                    primary.Pixels.Count),
            index =>
                primary.Pixels[index] !=
                influences.Pixels[index]);
    }

    private static BiomeDefinition[] Definitions() =>
    [
        Biome(
            "asteria:test/a",
            1f),
        Biome(
            "asteria:test/b",
            0.8f),
        Biome(
            "asteria:test/c",
            1.2f),
    ];

    private static BiomeDefinition Biome(
        string id,
        float weight) =>
        new(
            id,
            new BiomeSurfaceLayoutDefinition(
                weight,
                regionMin: 192,
                regionMax: 384),
            new BiomeTerrainDefinition(
                8f,
                0f,
                128,
                0f,
                32),
            [
                new BiomeSurfaceLayerDefinition(
                    "asteria:stone"),
            ]);

    private static DimensionDefinition Dimension(
        IEnumerable<string> biomeIds) =>
        new(
            new DimensionId(
                "asteria:test"),
            biomeIds,
            seaLevel: 32,
            gravityStrength: 18f,
            spawn:
                new DimensionSpawnDefinition(
                    0,
                    0),
            environment:
                new DimensionEnvironmentDefinition(
                    new DimensionColor(
                        0,
                        0,
                        0),
                    new DimensionColor(
                        255,
                        255,
                        255),
                    1f,
                    new DimensionColor(
                        0,
                        0,
                        0),
                    0f));
}
