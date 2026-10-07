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
        Assert.DoesNotContain(
            "asteria:overworld/desert",
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
        Assert.Contains(
            "asteria:overworld/ocean",
            biomes.Get(
                    "asteria:overworld/wasteland")
                .SurfaceLayout!
                .CannotBorder);
        Assert.Contains(
            "asteria:overworld/desert",
            biomes.Get(
                    "asteria:overworld/enchanted_forest")
                .SurfaceLayout!
                .CannotBorder);
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
