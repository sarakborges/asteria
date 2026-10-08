using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BiomePlacementParityTests
{
    [Fact]
    public void SpawnWeightIsIndependentFromLayoutWeight()
    {
        var stone =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var neverSpawn =
            SurfaceBiome(
                "asteria:test/a",
                weight: 9f,
                spawnWeight: 0f);
        var spawn =
            SurfaceBiome(
                "asteria:test/b",
                weight: 1f,
                spawnWeight: 1f);
        var ocean =
            SurfaceBiome(
                "asteria:test/ocean",
                weight: 1f,
                spawnWeight: 100f);
        var registry =
            new BiomeRegistry(
            [
                neverSpawn,
                spawn,
                ocean,
            ]);
        registry.ValidateBlocks(
            stone);

        var field =
            new BiomeField(
                0xA57E_2026UL,
                [
                    neverSpawn.Id,
                    spawn.Id,
                    ocean.Id,
                ],
                registry);

        Assert.Equal(
            spawn.Id,
            field.SelectSpawnBiome(
                ocean.Id));
        Assert.Equal(
            9f,
            neverSpawn.SurfaceLayout!.Weight);
        Assert.Equal(
            0f,
            neverSpawn.SurfaceLayout.SpawnWeight);
    }

    [Fact]
    public void TinyPositiveSpawnWeightRemainsEligible()
    {
        var registry =
            new BiomeRegistry(
            [
                SurfaceBiome(
                    "asteria:test/tiny",
                    weight: 1f,
                    spawnWeight: 0.00001f),
                SurfaceBiome(
                    "asteria:test/disabled",
                    weight: 1f,
                    spawnWeight: 0f),
            ]);
        var field =
            new BiomeField(
                17UL,
                [
                    "asteria:test/tiny",
                    "asteria:test/disabled",
                ],
                registry);

        Assert.Equal(
            "asteria:test/tiny",
            field.SelectSpawnBiome());
    }

    [Fact]
    public void BiomeJsonParsesSpawnWeight()
    {
        var definition =
            BiomeDefinitionJson.Parse(
                """
                {
                  "id": "asteria:test/weighted",
                  "surfaceLayout": {
                    "weight": 1.25,
                    "spawnWeight": 0.5,
                    "regionSize": {
                      "min": 192,
                      "max": 384
                    }
                  },
                  "surfaceTerrain": {
                    "baseHeightOffset": 0,
                    "macroAmplitude": 0,
                    "macroScale": 64,
                    "detailAmplitude": 0,
                    "detailScale": 32
                  },
                  "surfaceLayers": [
                    {
                      "block": "asteria:stone"
                    }
                  ]
                }
                """);

        Assert.Equal(
            1.25f,
            definition.SurfaceLayout!.Weight);
        Assert.Equal(
            0.5f,
            definition.SurfaceLayout.SpawnWeight);
    }

    [Fact]
    public void DefaultPackRestoresMineClonePlacementPolicy()
    {
        var biomes =
            LoadDefaultBiomes();
        var dimensions =
            LoadDefaultDimensions();
        var overworld =
            dimensions.Get(
                DimensionId.Overworld);

        Assert.Contains(
            "asteria:overworld/enchanted_forest",
            overworld.SurfaceBiomes);

        AssertLayout(
            biomes,
            "asteria:overworld/plains",
            1f,
            1f);
        AssertLayout(
            biomes,
            "asteria:overworld/swamp",
            0.9f,
            0.5f);
        AssertLayout(
            biomes,
            "asteria:overworld/arctic",
            0.9f,
            0.5f);
        AssertLayout(
            biomes,
            "asteria:overworld/desert",
            1f,
            0.5f);
        AssertLayout(
            biomes,
            "asteria:overworld/wasteland",
            1.3f,
            1f);
        AssertLayout(
            biomes,
            "asteria:overworld/enchanted_forest",
            1.25f,
            1f);
        AssertLayout(
            biomes,
            "asteria:overworld/mountains",
            0.85f,
            0.5f);
        AssertLayout(
            biomes,
            "asteria:overworld/gorge",
            1f,
            0.5f);
        AssertLayout(
            biomes,
            "asteria:overworld/alps",
            1f,
            0.5f);
        AssertLayout(
            biomes,
            "asteria:overworld/mountain_belt",
            1f,
            0.5f);
        AssertLayout(
            biomes,
            "asteria:overworld/volcano",
            1f,
            0.5f);
        AssertLayout(
            biomes,
            "asteria:overworld/ocean",
            1f,
            1f);

        AssertLayout(
            biomes,
            "asteria:umbral/umbral_reach",
            1f,
            1f);
        AssertLayout(
            biomes,
            "asteria:umbral/withered_waste",
            1.3f,
            1f);
        AssertLayout(
            biomes,
            "asteria:umbral/wraith_grove",
            1.25f,
            1f);

        Assert.Equal(
            0.25f,
            biomes.Get(
                    "asteria:overworld/floating_islands")
                .VolumeLayout!
                .Weight);

        var gorge =
            biomes.Get(
                    "asteria:overworld/gorge")
                .SurfaceLayout!;
        Assert.Equal(
            (192u, 320u),
            (
                gorge.RegionMin,
                gorge.RegionMax));

        var mountainGroup =
            new[]
            {
                "asteria:overworld/mountains",
                "asteria:overworld/gorge",
                "asteria:overworld/alps",
                "asteria:overworld/mountain_belt",
                "asteria:overworld/volcano",
            };

        foreach (var biomeId in mountainGroup)
        {
            var denied =
                biomes.Get(
                        biomeId)
                    .SurfaceLayout!
                    .CannotBorder;

            foreach (var other in
                     mountainGroup)
            {
                if (string.Equals(
                        biomeId,
                        other,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                Assert.Contains(
                    other,
                    denied);
            }
        }

        var swamp =
            biomes.Get(
                    "asteria:overworld/swamp")
                .SurfaceLayout!;
        // Asteria intentionally excludes desert and wasteland from swamp
        // borders. MineClone's original permissive adjacency was adapted.
        Assert.Contains(
            "asteria:overworld/desert",
            swamp.CannotBorder);
        Assert.Contains(
            "asteria:overworld/wasteland",
            swamp.CannotBorder);
        Assert.All(
            mountainGroup,
            id =>
                Assert.Contains(
                    id,
                    swamp.CannotBorder));

        Assert.Contains(
            "asteria:overworld/volcano",
            biomes.Get(
                    "asteria:overworld/arctic")
                .SurfaceLayout!
                .CannotBorder);
        // The ocean itself owns its shoreline, so wasteland may border
        // ocean while the incompatible arctic/wasteland pairing is rejected.
        var wastelandBorders = biomes.Get(
                "asteria:overworld/wasteland")
            .SurfaceLayout!
            .CannotBorder;
        Assert.Contains(
            "asteria:overworld/arctic",
            wastelandBorders);
        Assert.DoesNotContain(
            "asteria:overworld/ocean",
            wastelandBorders);
        Assert.Contains(
            "asteria:overworld/desert",
            biomes.Get(
                    "asteria:overworld/enchanted_forest")
                .SurfaceLayout!
                .CannotBorder);
    }

    [Fact]
    public void AuthoredBlendCurveChangesInfluenceWithoutChangingPrimaryBiome()
    {
        var registry = new BiomeRegistry(
        [
            SurfaceBiome("asteria:test/a", 1f, 1f),
            SurfaceBiome("asteria:test/b", 1f, 1f),
        ]);
        var narrow = new BiomeField(
            121UL,
            ["asteria:test/a", "asteria:test/b"],
            registry,
            blending: new BiomeBlendingDefinition(scoreBand: 0.05));
        var wide = new BiomeField(
            121UL,
            ["asteria:test/a", "asteria:test/b"],
            registry,
            blending: new BiomeBlendingDefinition(scoreBand: 1d));
        var mixedNarrow = 0;
        var mixedWide = 0;

        for (var z = -1024; z < 1024; z += 96)
        {
            for (var x = -1024; x < 1024; x += 96)
            {
                var a = narrow.Sample(x, z);
                var b = wide.Sample(x, z);
                Assert.Equal(a.Primary, b.Primary);
                mixedNarrow += a.Influences.Count > 1 ? 1 : 0;
                mixedWide += b.Influences.Count > 1 ? 1 : 0;
            }
        }

        Assert.True(
            mixedWide > mixedNarrow,
            "Increasing scoreBand should admit more neighboring biome influences.");
    }

    [Fact]
    public void BlendCurvesAndParametersAreValidated()
    {
        var linear = new BiomeBlendingDefinition(
            influenceCurve: BiomeInfluenceCurve.Linear);
        var smooth = new BiomeBlendingDefinition(
            influenceCurve: BiomeInfluenceCurve.SmoothStep);
        var smoother = new BiomeBlendingDefinition(
            influenceCurve: BiomeInfluenceCurve.SmootherStep);

        Assert.Equal(0.25d, linear.WeightAt(0.25d));
        Assert.True(smoother.WeightAt(0.25d) <
                    smooth.WeightAt(0.25d));
        Assert.True(smooth.WeightAt(0.25d) <
                    linear.WeightAt(0.25d));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeBlendingDefinition(scoreBand: 0d));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeBlendingDefinition(jitterFraction: 0.6d));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeBlendingDefinition(
                coarseWarpStrength: 0.6d,
                fineWarpStrength: 0.3d));
    }

    [Fact]
    public void AuthoredContourHarmonicsChangeShapesButRemainDeterministic()
    {
        var registry = new BiomeRegistry(
        [
            SurfaceBiome("asteria:test/a", 1f, 1f),
            SurfaceBiome("asteria:test/b", 1f, 1f),
        ]);
        var original = new BiomeField(
            121UL, ["asteria:test/a", "asteria:test/b"], registry);
        var authored = new BiomeBlendingDefinition(
            contourHarmonics:
            [
                new BiomeContourHarmonicDefinition(2, 0.3d),
                new BiomeContourHarmonicDefinition(7, 0.2d),
            ],
            sizeExponent: 0.25d,
            seedBiasAmplitude: 0.1d,
            continuationBonus: 0.02d);
        var different = new BiomeField(
            121UL, ["asteria:test/a", "asteria:test/b"], registry,
            blending: authored);
        var repeat = new BiomeField(
            121UL, ["asteria:test/a", "asteria:test/b"], registry,
            blending: authored);
        var changed = 0;

        for (var z = -1024; z <= 1024; z += 96)
        {
            for (var x = -1024; x <= 1024; x += 96)
            {
                var before = original.Sample(x, z);
                var after = different.Sample(x, z);
                Assert.Equal(after.Primary, repeat.Sample(x, z).Primary);
                changed += before.Primary != after.Primary ? 1 : 0;
            }
        }

        Assert.True(changed > 0);
        Assert.Equal(2, authored.ContourHarmonics.Count);
    }

    [Fact]
    public void ContourParametersRejectInvalidHarmonicGeometry()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeContourHarmonicDefinition(0, 0.1d));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeContourHarmonicDefinition(3, double.NaN));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeBlendingDefinition(
                contourHarmonics:
                [
                    new BiomeContourHarmonicDefinition(3, 0.4d),
                    new BiomeContourHarmonicDefinition(5, 0.4d),
                ]));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeBlendingDefinition(sizeExponent: -0.1d));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeBlendingDefinition(
                seedBiasAmplitude: double.PositiveInfinity));
    }

    [Fact]
    public void ShoreProfileSamplesAreOrderedAndBounded()
    {
        var profile = new DimensionShoreProfileDefinition(
        [
            new DimensionShoreSampleDefinition(0d, 0d, 0d),
            new DimensionShoreSampleDefinition(0.5d, 2d, 1d),
            new DimensionShoreSampleDefinition(1d, 0d, 0d),
        ]);
        Assert.Equal(3, profile.Samples.Count);

        Assert.ThrowsAny<ArgumentException>(() =>
            new DimensionShoreProfileDefinition(
            [
                new DimensionShoreSampleDefinition(0d, 0d, 0d),
                new DimensionShoreSampleDefinition(0.5d, 0d, 1d),
                new DimensionShoreSampleDefinition(0.5d, 1d, 0d),
                new DimensionShoreSampleDefinition(1d, 0d, 0d),
            ]));
        Assert.ThrowsAny<ArgumentException>(() =>
            new DimensionShoreSampleDefinition(0.5d, 0d, -0.1d));
    }

    private static void AssertLayout(
        BiomeRegistry biomes,
        string biomeId,
        float weight,
        float spawnWeight)
    {
        var layout =
            biomes.Get(
                    biomeId)
                .SurfaceLayout!;

        Assert.Equal(
            weight,
            layout.Weight);
        Assert.Equal(
            spawnWeight,
            layout.SpawnWeight);
    }

    private static BiomeDefinition SurfaceBiome(
        string id,
        float weight,
        float spawnWeight) =>
        new(
            id,
            new BiomeSurfaceLayoutDefinition(
                weight: weight,
                regionMin: 192,
                regionMax: 384,
                spawnWeight: spawnWeight),
            new BiomeTerrainDefinition(
                baseHeightOffset: 0f,
                macroAmplitude: 0f,
                macroScale: 64,
                detailAmplitude: 0f,
                detailScale: 32),
            [
                new BiomeSurfaceLayerDefinition(
                    "asteria:stone"),
            ]);

    private static BiomeRegistry LoadDefaultBiomes()
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "biomes");

        return BiomeRegistry.FromJson(
            Directory
                .EnumerateFiles(
                    directory,
                    "*.json")
                .OrderBy(
                    path => path,
                    StringComparer.Ordinal)
                .Select(
                    File.ReadAllText));
    }

    private static DimensionRegistry LoadDefaultDimensions()
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "dimensions");

        return DimensionRegistry.FromJson(
            Directory
                .EnumerateFiles(
                    directory,
                    "*.json")
                .OrderBy(
                    path => path,
                    StringComparer.Ordinal)
                .Select(
                    File.ReadAllText));
    }
}
