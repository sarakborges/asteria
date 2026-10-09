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

    [Theory]
    [InlineData("swamp", "willow_grove", "open_mire", "fungal_ground")]
    [InlineData("enchanted_forest", "luminous_clearing", "violet_undergrowth", "pink_glade")]
    public void OverworldBiomesAuthorDistinctHabitatRegions(
        string biomeFile, string firstBand, string middleBand, string lastBand)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "packs", "default",
            "data", "biomes", biomeFile + ".json");
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(path));
        var definition = Assert.IsType<SurfaceHabitatDefinition>(biome.SurfaceHabitats);
        Assert.Equal(new[] { firstBand, middleBand, lastBand },
            definition.Bands.Select(band => band.Id));
        Assert.All(biome.Decorations, decoration =>
        {
            var weights = Assert.IsType<SurfaceHabitatWeights>(decoration.HabitatWeights);
            definition.ValidateWeights(weights);
        });

        var first = new SurfaceHabitatField(741UL, [biome]);
        var second = new SurfaceHabitatField(741UL, [biome]);
        foreach (var (x, z) in new[] { (-213, -161), (-16, 23), (85, 272), (401, -370) })
            Assert.Equal(first.Sample(biome.Id, x, z),
                         second.Sample(biome.Id, x, z));
    }

    [Fact]
    public void SwampHabitatsDistributeWillowsFungiAndSurfaceObjects()
    {
        var biomePath = Path.Combine(AppContext.BaseDirectory, "packs", "default",
            "data", "biomes", "swamp.json");
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(biomePath));
        var rootPath = Path.Combine(AppContext.BaseDirectory, "packs", "default",
            "data", "dimensions", "overworld.json");
        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(rootPath));
        var roots = dimension.GeneratedSurfaceStructures
            .Where(rule => rule.Biome == biome.Id).ToArray();
        Assert.Equal(3, roots.Length);
        var willows = Assert.Single(roots,
            root => root.Structure == "asteria:tree_willow");
        Assert.True(willows.HabitatWeights!.For("willow_grove") >
                    willows.HabitatWeights.For("open_mire"));
        var boulder = Assert.Single(roots,
            root => root.Structure == "asteria:boulder_small");
        Assert.True(boulder.HabitatWeights!.For("fungal_ground") >
                    boulder.HabitatWeights.For("open_mire"));
        var mushroom = Assert.Single(biome.Decorations,
            rule => rule.Block == "asteria:mushroom_brown");
        Assert.True(mushroom.HabitatWeights!.For("fungal_ground") >
                    mushroom.HabitatWeights.For("willow_grove"));
        Assert.Equal(93, mushroom.Conditions!.MaxY);
    }

    [Fact]
    public void EnchantedForestHabitatsKeepExistingMushroomPaletteAndNoInventedTrees()
    {
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data",
            "biomes", "enchanted_forest.json")));
        var purple = Assert.Single(biome.Decorations,
            rule => rule.Block == "asteria:mushroom_purple");
        var pink = Assert.Single(biome.Decorations,
            rule => rule.Block == "asteria:mushroom_pink");
        Assert.True(purple.HabitatWeights!.For("violet_undergrowth") >
                    purple.HabitatWeights.For("pink_glade"));
        Assert.True(pink.HabitatWeights!.For("pink_glade") >
                    pink.HabitatWeights.For("violet_undergrowth"));
    }

    [Fact]
    public void WraithGroveHabitatsWorkOnAnotherSphereAndEnableGroundMushrooms()
    {
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(Path.Combine(
            AppContext.BaseDirectory, "packs", "default", "data",
            "biomes", "wraith_grove.json")));
        Assert.Equal("asteria:umbral/wraith_grove", biome.Id);
        var bands = Assert.IsType<SurfaceHabitatDefinition>(biome.SurfaceHabitats);
        Assert.Equal(new[] { "dusky_underbrush", "pale_clearing", "decay_pockets" },
            bands.Bands.Select(band => band.Id));
        Assert.All(biome.Decorations, rule =>
            bands.ValidateWeights(Assert.IsType<SurfaceHabitatWeights>(rule.HabitatWeights)));
        var mushrooms = Assert.Single(biome.Decorations,
            rule => rule.Block == "asteria:mushroom_brown");
        Assert.Contains("asteria:grass_block", mushrooms.SurfaceBlocks);
        Assert.True(mushrooms.HabitatWeights!.For("decay_pockets") >
                    mushrooms.HabitatWeights.For("pale_clearing"));
        var first = new SurfaceHabitatField(41UL, [biome]);
        var second = new SurfaceHabitatField(41UL, [biome]);
        Assert.Equal(first.Sample(biome.Id, -289, 74),
                     second.Sample(biome.Id, -289, 74));
    }

    [Fact]
    public void WraithTreeAndSnagRootsFollowTheirAuthoredHabitats()
    {
        var biomePath = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", "biomes", "wraith_grove.json");
        var biome = BiomeDefinitionJson.Parse(File.ReadAllText(biomePath));
        var dimensionPath = Path.Combine(AppContext.BaseDirectory, "packs",
            "default", "data", "dimensions", "umbral.json");
        var dimension = DimensionDefinitionJson.Parse(File.ReadAllText(dimensionPath));
        var roots = dimension.GeneratedSurfaceStructures
            .Where(root => root.Biome == biome.Id).ToArray();
        Assert.Equal(3, roots.Length);
        Assert.All(roots, rule =>
            biome.SurfaceHabitats!.ValidateWeights(rule.HabitatWeights));

        var tree = Assert.Single(roots,
            root => root.Structure == "asteria:tree_wraith");
        var grove = Assert.Single(roots,
            root => root.Structure == "asteria:wraith_grove");
        var snag = Assert.Single(roots,
            root => root.Structure == "asteria:wraith_snag");

        Assert.True(tree.HabitatWeights!.For("dusky_underbrush") >
                    tree.HabitatWeights.For("pale_clearing"));
        Assert.Equal(0f, grove.HabitatWeights!.For("pale_clearing"));
        Assert.True(snag.HabitatWeights!.For("decay_pockets") >
                    snag.HabitatWeights.For("dusky_underbrush"));

        var a = new SurfaceHabitatField(53UL, [biome]);
        var b = new SurfaceHabitatField(53UL, [biome]);
        foreach (var (x, z) in new[] { (-281, 71), (0, 0), (299, -112) })
        {
            Assert.Equal(a.Sample(biome.Id, x, z),
                         b.Sample(biome.Id, x, z));
            Assert.Equal(a.Weight(biome.Id, tree.HabitatWeights, x, z),
                         b.Weight(biome.Id, tree.HabitatWeights, x, z));
        }
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
