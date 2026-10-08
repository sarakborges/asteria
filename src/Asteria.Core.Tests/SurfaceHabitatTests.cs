using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SurfaceHabitatTests
{
    private const string BiomeId = "asteria:test/plains";

    [Fact]
    public void HabitatBandWeightsBlendWithoutASeam()
    {
        var profile = new SurfaceHabitatDefinition(96, 0.2f,
        [
            new SurfaceHabitatBand("grove", 0f),
            new SurfaceHabitatBand("open", 1f),
        ]);
        var weights = Weights(("grove", 2f), ("open", 0f));
        Assert.Equal(2d, profile.Weight(-0.2, weights));
        Assert.Equal(0d, profile.Weight(0.2, weights));
        Assert.InRange(profile.Weight(0d, weights), 0.99999d, 1.00001d);
        Assert.True(profile.Weight(-0.01, weights) > profile.Weight(0.01, weights));
    }

    [Fact]
    public void SharedHabitatNoiseDeterministicallyControlsDecorationDensity()
    {
        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:grass_block"),
            new BlockDefinition("asteria:stick"),
        ]);
        var biome = TestBiome(Weights(("grove", 1f)));
        var habitats = new SurfaceHabitatField(779UL, [biome]);
        var field = new SurfaceDecorationField(779UL, [biome], blocks,
            habitats: habitats);
        var another = new SurfaceDecorationField(779UL, [biome], blocks);
        var sample = new BiomeSample(BiomeId,
            [new BiomeInfluence(BiomeId, 1f)]);
        var grass = blocks.GetId("asteria:grass_block");
        var stick = blocks.GetId("asteria:stick");
        var foundOpen = false;
        var foundGrove = false;

        for (var z = -256; z <= 256; z += 8)
        for (var x = -256; x <= 256; x += 8)
        {
            var noise = habitats.Sample(BiomeId, x, z);
            var actual = field.BlockAt(sample, grass, x, z);
            Assert.Equal(actual, another.BlockAt(sample, grass, x, z));
            if (noise < -0.08)
            {
                foundOpen = true;
                Assert.Equal(stick, actual);
            }
            else if (noise > 0.08)
            {
                foundGrove = true;
                Assert.True(actual.IsAir);
            }
        }

        Assert.True(foundOpen);
        Assert.True(foundGrove);
    }

    [Fact]
    public void AuthoredPlainsHasSharedBandsAndValidWeights()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", "biomes", "plains.json");
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(path));
        var regions = Assert.IsType<SurfaceHabitatDefinition>(biome.SurfaceHabitats);
        Assert.Equal(4, regions.Bands.Count);
        Assert.Equal(new[] {"grove", "open", "thicket", "rocky"},
            regions.Bands.Select(band => band.Id));
        Assert.All(biome.Decorations, rule =>
        {
            Assert.NotNull(rule.HabitatWeights);
            regions.ValidateWeights(rule.HabitatWeights);
        });
        var field = new SurfaceHabitatField(741UL, [biome]);
        var another = new SurfaceHabitatField(741UL, [biome]);
        foreach (var (x, z) in new[] {(-137, -87), (3, 4), (226, -120)})
            Assert.Equal(field.Sample(biome.Id, x, z),
                         another.Sample(biome.Id, x, z));
    }

    [Fact]
    public void RejectInvalidBandsAndMissingHabitatReferences()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new SurfaceHabitatDefinition(7, 0f,
            [
                new SurfaceHabitatBand("open", 0f),
                new SurfaceHabitatBand("grove", 1f),
            ]));
        Assert.ThrowsAny<ArgumentException>(() =>
            new SurfaceHabitatDefinition(64, .2f,
            [
                new SurfaceHabitatBand("open", 0f),
                new SurfaceHabitatBand("open", 1f),
            ]));
        Assert.ThrowsAny<ArgumentException>(() =>
            new SurfaceHabitatDefinition(64, .3f,
            [
                new SurfaceHabitatBand("open", 0f),
                new SurfaceHabitatBand("grove", .1f),
                new SurfaceHabitatBand("rocky", 1f),
            ]));
        Assert.ThrowsAny<ArgumentException>(() =>
            TestBiome(Weights(("unknown", 1f))));
        Assert.ThrowsAny<ArgumentException>(() =>
            new SurfaceHabitatWeights(
                [new KeyValuePair<string, float>("grove", float.NaN)]));
        Assert.Throws<FormatException>(() =>
            SurfaceHabitatDefinitionJson.ParseWeights(
                System.Text.Json.JsonDocument.Parse(
                    """{"habitatWeights":{"grove":"lots"}}""").RootElement));
    }

    private static BiomeDefinition TestBiome(SurfaceHabitatWeights weights) =>
        new(
            BiomeId,
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(0, 0, 64, 0, 32),
            [new BiomeSurfaceLayerDefinition("asteria:grass_block")],
            decorations:
            [
                new BiomeDecorationDefinition("asteria:stick", 1f,
                    ["asteria:grass_block"], habitatWeights: weights),
            ],
            surfaceHabitats: new SurfaceHabitatDefinition(64, .1f,
            [
                new SurfaceHabitatBand("grove", 0f),
                new SurfaceHabitatBand("open", 1f),
            ]));

    private static SurfaceHabitatWeights Weights(params (string Id, float Weight)[] values) =>
        new(values.Select(value =>
            new KeyValuePair<string, float>(value.Id, value.Weight)));
}
