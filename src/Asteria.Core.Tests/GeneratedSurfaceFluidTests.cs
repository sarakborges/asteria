using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class GeneratedSurfaceFluidTests
{
    [Fact]
    public void DimensionJsonParsesGeneratedSurfaceFluidRules()
    {
        var definition =
            DimensionDefinitionJson.Parse(
                """
                {
                  "id": "asteria:test",
                  "seaLevel": 32,
                  "gravityStrength": 18,
                  "spawn": { "x": 0, "z": 0 },
                  "environment": {
                    "backgroundColor": "000000",
                    "ambientColor": "FFFFFF",
                    "ambientEnergy": 1,
                    "fogColor": "000000",
                    "fogDensity": 0
                  },
                  "surfaceBiomes": [
                    "asteria:test/swamp"
                  ],
                  "generatedSurfaceFluids": [
                    {
                      "biome": "asteria:test/swamp",
                      "fluid": "asteria:water",
                      "spacing": 18,
                      "radius": 5,
                      "jitter": 3,
                      "chance": 0.75,
                      "depth": 1
                    }
                  ]
                }
                """);

        var rule =
            Assert.Single(
                definition.GeneratedSurfaceFluids);

        Assert.Equal(
            "asteria:test/swamp",
            rule.Biome);
        Assert.Equal(
            "asteria:water",
            rule.Fluid);
        Assert.Equal(
            18,
            rule.Spacing);
        Assert.Equal(
            5,
            rule.Radius);
        Assert.Equal(
            3,
            rule.Jitter);
        Assert.Equal(
            0.75f,
            rule.Chance);
        Assert.Equal(
            1,
            rule.Depth);
    }

    [Fact]
    public void SurfaceFluidCutsTerrainAndMaterializesTheSamePatch()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(
                        0,
                        96,
                        255),
                    0.7f),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/swamp",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0f,
                    0f,
                    64,
                    0f,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var dimension =
            new DimensionDefinition(
                new DimensionId(
                    "asteria:test"),
                [
                    biome.Id,
                ],
                32,
                18f,
                new DimensionSpawnDefinition(
                    0,
                    0),
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
                    0f),
                generatedSurfaceFluids:
                [
                    new DimensionGeneratedSurfaceFluidDefinition(
                        biome.Id,
                        "asteria:water",
                        spacing: 16,
                        radius: 8,
                        jitter: 0,
                        chance: 1f,
                        depth: 2),
                ]);
        var generator =
            new BiomeWorldGenerator(
                77UL,
                dimension,
                blocks,
                fluids,
                new BiomeRegistry(
                [
                    biome,
                ]));

        Assert.Equal(
            30,
            generator.SurfaceHeight(
                8,
                8));
        Assert.Equal(
            32,
            generator.SurfaceHeight(
                0,
                0));

        var lower =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    1,
                    0));
        var upper =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));
        var water =
            fluids.GetId(
                "asteria:water");

        Assert.Equal(
            blocks.GetId(
                "asteria:stone"),
            lower.GetBlock(
                8,
                14,
                8));
        Assert.Equal(
            water,
            lower.GetFluid(
                    8,
                    15,
                    8)
                .Fluid);
        Assert.Equal(
            water,
            upper.GetFluid(
                    8,
                    0,
                    8)
                .Fluid);
        Assert.True(
            upper.GetFluid(
                    0,
                    0,
                    0)
                .IsEmpty);
    }

    [Fact]
    public void DefaultOverworldRestoresSwampAndVolcanoSurfaceFluids()
    {
        var dimensions =
            DimensionRegistry.FromJson(
                ReadJsonDirectory(
                    "dimensions"));
        var overworld =
            dimensions.Get(
                DimensionId.Overworld);

        Assert.Equal(
            2,
            overworld.GeneratedSurfaceFluids.Count);

        var swamp =
            Assert.Single(
                overworld.GeneratedSurfaceFluids,
                rule =>
                    string.Equals(
                        rule.Biome,
                        "asteria:overworld/swamp",
                        StringComparison.Ordinal));
        Assert.Equal(
            "asteria:water",
            swamp.Fluid);
        Assert.Equal(
            (18, 5, 3, 0.75f, 1),
            (
                swamp.Spacing,
                swamp.Radius,
                swamp.Jitter,
                swamp.Chance,
                swamp.Depth));

        var volcano =
            Assert.Single(
                overworld.GeneratedSurfaceFluids,
                rule =>
                    string.Equals(
                        rule.Biome,
                        "asteria:overworld/volcano",
                        StringComparison.Ordinal));
        Assert.Equal(
            "asteria:lava",
            volcano.Fluid);
        Assert.Equal(
            (96, 10, 12, 0.4f, 2),
            (
                volcano.Spacing,
                volcano.Radius,
                volcano.Jitter,
                volcano.Chance,
                volcano.Depth));
    }

    private static IEnumerable<string> ReadJsonDirectory(
        string category)
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                category);

        return Directory
            .EnumerateFiles(
                directory,
                "*.json")
            .OrderBy(
                path => path,
                StringComparer.Ordinal)
            .Select(
                File.ReadAllText);
    }
}
