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
    public void DefaultOverworldUsesTerrainDrivenSurfaceFluids()
    {
        var dimensions =
            DimensionRegistry.FromJson(
                ReadJsonDirectory(
                    "dimensions"));
        var biomes =
            BiomeRegistry.FromJson(
                ReadJsonDirectory(
                    "biomes"));
        var fluids =
            FluidRegistry.FromJson(
                ReadJsonDirectory(
                    "fluids"));
        var overworld =
            dimensions.Get(
                DimensionId.Overworld);

        Assert.Empty(
            overworld.GeneratedSurfaceFluids);
        biomes.ValidateFluids(
            fluids);

        var swamp =
            biomes.Get(
                "asteria:overworld/swamp");
        _ =
            Assert.IsType<
                BiomeRollingTerrainShapeDefinition>(
                swamp.SurfaceTerrain!.Shape);
        var depressions =
            Assert.IsType<BiomeDepressionsTerrainModifierDefinition>(
                Assert.Single(swamp.SurfaceTerrain.Modifiers));
        Assert.Equal(4.8f, depressions.Depth);
        Assert.True(
            swamp.SurfaceTerrain.FillToSeaLevel);
        Assert.Equal(
            SurfaceHeightInfluencePolicy.LowerOnly,
            swamp.SurfaceTerrain.InfluencePolicy);

        var volcano =
            biomes.Get(
                "asteria:overworld/volcano");
        var terrain =
            Assert.IsType<
                BiomeConeTerrainShapeDefinition>(
                volcano.SurfaceTerrain!.Shape);
        var crater =
            Assert.IsType<BiomeCraterDefinition>(
                volcano.SurfaceTerrain.Crater);
        var fill = Assert.IsType<BiomeCraterFluidFillDefinition>(
            crater.FluidFill);
        var spill = Assert.IsType<BiomeCraterSpillDefinition>(fill.Spill);

        Assert.Equal(
            (12f, 72f, 38f, 0.14f),
            (
                terrain.BaseHeight,
                terrain.Height,
                crater.Depth,
                crater.Radius));
        Assert.Equal(
            "asteria:lava",
            fill.Fluid);
        Assert.Equal(
            (0.91f, 54f, 0.56f, 0.9f, 0.012f, 0.055f, (byte)6),
            (
                fill.MinimumStrength,
                fill.TopLevel,
                spill.MinimumStrength,
                spill.MaximumStrength,
                spill.Scale,
                spill.Width,
                spill.Level));
    }

    [Fact]
    public void CraterFillSupportsNonConeTerrain()
    {
        var biome = BiomeDefinitionJson.Parse(
            """
            {
              "id": "asteria:test/hills",
              "surfaceLayout": {},
              "surfaceTerrain": {
                "type": "rolling",
                "baseHeight": 10,
                "amplitude": 0,
                "scale": 0.01,
                "detailAmplitude": 0,
                "detailScale": 0.02,
                "crater": {
                  "depth": 6,
                  "radius": 0.3,
                  "irregularity": 0,
                  "noiseScale": 0.02,
                  "transitionWidth": 0.01,
                  "fluidFill": {
                    "fluid": "asteria:water",
                    "minimumStrength": 0.8,
                    "topLevel": 11
                  }
                }
              },
              "surfaceLayers": [{ "block": "asteria:stone" }]
            }
            """);

        Assert.IsType<BiomeRollingTerrainShapeDefinition>(
            biome.SurfaceTerrain!.Shape);
        Assert.NotNull(biome.SurfaceTerrain.Crater);

        var fluids = new FluidRegistry(
        [
            new FluidDefinition(
                "asteria:water",
                new FluidColor(20, 80, 240),
                0.7f),
        ]);
        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"),
            [biome.Id],
            32,
            18f,
            new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255),
                1f,
                new DimensionColor(0, 0, 0),
                0f));
        var field = new GeneratedFluidField(
            71UL, dimension, fluids, [biome]);
        var sample = new BiomeSample(
            biome.Id,
            [new BiomeInfluence(biome.Id, 1f, 1f)]);
        Assert.True(field.TryGetColumnBounds(
            sample, 35, 0, 0, 0, out var minY, out var maxY));
        Assert.Equal(36, minY);
        Assert.Equal(42, maxY);
        Assert.Equal(
            fluids.GetId("asteria:water"),
            field.FluidAtEmptyVoxel(sample, 35, 0, 0, 40, 0).Fluid);

        Assert.Throws<ArgumentException>(() =>
            new BiomeCraterFluidFillDefinition(
                "asteria:water",
                0.5f,
                11f,
                new BiomeCraterSpillDefinition(
                    0.4f, 0.8f, 0.02f, 0.03f, 6)));
    }

    [Fact]
    public void LegacySpecialSurfaceFluidContractIsRejected()
    {
        Assert.Throws<FormatException>(() =>
            BiomeDefinitionJson.Parse(
                """
                {
                  "id": "asteria:test/volcano",
                  "surfaceFluid": { "type": "volcano_crater" }
                }
                """));
    }

    [Fact]
    public void SwampTerrainUsesStaticSeaFill()
    {
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(
                        79,
                        159,
                        214),
                    0.72f),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/swamp",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    new BiomeRollingTerrainShapeDefinition(
                        baseHeight: 0.9f,
                        amplitude: 0.7f,
                        scale: 0.0065f,
                        detailAmplitude: 0.4f,
                        detailScale: 0.045f),
                    fillToSeaLevel: true),
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
                seaLevel: 32,
                gravityStrength: 18f,
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
                generatedOcean:
                    new DimensionGeneratedOceanDefinition(
                        biome.Id,
                        "asteria:water"));
        var field =
            new GeneratedFluidField(
                91UL,
                dimension,
                fluids,
                [
                    biome,
                ]);
        var sample =
            new BiomeSample(
                biome.Id,
                [
                    new BiomeInfluence(
                        biome.Id,
                        1f,
                        1f),
                ]);

        Assert.True(
            field.TryGetColumnBounds(
                sample,
                baseSurfaceY: 28,
                surfaceCutDepth: 0,
                worldX: 7,
                worldZ: 11,
                out var minimumY,
                out var maximumY));
        Assert.Equal(
            29,
            minimumY);
        Assert.Equal(
            32,
            maximumY);
        Assert.Equal(
            fluids.GetId(
                "asteria:water"),
            field.FluidAtEmptyVoxel(
                    sample,
                    28,
                    0,
                    7,
                    32,
                    11)
                .Fluid);
        Assert.True(
            field.FluidAtEmptyVoxel(
                    sample,
                    28,
                    0,
                    7,
                    33,
                    11)
                .IsEmpty);
    }

    [Fact]
    public void DefaultBiomePackUsesReusableTerrainTypes()
    {
        var biomes =
            BiomeRegistry.FromJson(
                ReadJsonDirectory(
                    "biomes"));

        var expected =
            new Dictionary<string, Type>(
                StringComparer.Ordinal)
            {
                ["asteria:overworld/plains"] =
                    typeof(BiomeRollingTerrainShapeDefinition),
                ["asteria:overworld/desert"] =
                    typeof(BiomeDunesTerrainShapeDefinition),
                ["asteria:overworld/ocean"] =
                    typeof(BiomeRollingTerrainShapeDefinition),
                ["asteria:overworld/swamp"] =
                    typeof(BiomeRollingTerrainShapeDefinition),
                ["asteria:overworld/mountains"] =
                    typeof(BiomeRidgesTerrainShapeDefinition),
                ["asteria:overworld/gorge"] =
                    typeof(BiomeValleyTerrainShapeDefinition),
                ["asteria:overworld/alps"] =
                    typeof(BiomeRidgesTerrainShapeDefinition),
                ["asteria:overworld/mountain_belt"] =
                    typeof(BiomeRidgesTerrainShapeDefinition),
                ["asteria:overworld/volcano"] =
                    typeof(BiomeConeTerrainShapeDefinition),
                ["asteria:umbral/umbral_reach"] =
                    typeof(BiomeRollingTerrainShapeDefinition),
                ["asteria:umbral/withered_waste"] =
                    typeof(BiomeRollingTerrainShapeDefinition),
                ["asteria:umbral/wraith_grove"] =
                    typeof(BiomeRollingTerrainShapeDefinition),
            };

        foreach (var (biomeId, terrainType) in
                 expected)
        {
            Assert.Equal(
                terrainType,
                biomes.Get(
                        biomeId)
                    .SurfaceTerrain!
                    .Shape
                    .GetType());
        }

        Assert.Equal(
            BiomeRidgeDetailMode.Ridged,
            Assert.IsType<BiomeRidgesTerrainShapeDefinition>(
                biomes.Get("asteria:overworld/alps").SurfaceTerrain!.Shape).DetailMode);
        Assert.Equal(
            BiomeRidgeDetailMode.Modulated,
            Assert.IsType<BiomeRidgesTerrainShapeDefinition>(
                biomes.Get("asteria:overworld/mountain_belt").SurfaceTerrain!.Shape).DetailMode);

        var mountains =
            biomes.Get(
                "asteria:overworld/mountains");
        _ =
            Assert.IsType<
                BiomeCliffsTerrainModifierDefinition>(
                Assert.Single(
                    mountains.SurfaceTerrain!
                        .Modifiers));
    }

    [Fact]
    public void VolcanoCoreMaterializesCraterLava()
    {
        var blocks =
            BlockRegistry.FromJson(
                ReadJsonDirectory(
                    "blocks"));
        var fluids =
            FluidRegistry.FromJson(
                ReadJsonDirectory(
                    "fluids"));
        var biomes =
            BiomeRegistry.FromJson(
                ReadJsonDirectory(
                    "biomes"));
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var dimensions =
            DimensionRegistry.FromJson(
                ReadJsonDirectory(
                    "dimensions"));
        var dimension =
            dimensions.Get(
                DimensionId.Overworld);
        var generator =
            new BiomeWorldGenerator(
                DimensionSeed.Derive(
                    0xA57E_2026UL,
                    dimension.Id),
                dimension,
                blocks,
                fluids,
                biomes,
                structures);
        var core =
            FindVolcanoCore(
                generator.Biomes);
        var surfaceY =
            generator.SurfaceHeight(
                core.X,
                core.Z);
        var lava =
            fluids.GetId(
                "asteria:lava");
        var foundLava =
            false;

        for (var worldY =
                 surfaceY +
                 1;
             worldY <=
                 surfaceY +
                 16;
             worldY++)
        {
            var address =
                VoxelCoordinates.FromWorld(
                    core.X,
                    worldY,
                    core.Z);
            var chunk =
                generator.Materialize(
                    address.Chunk);
            var fluid =
                chunk.GetFluid(
                    address.Local.X,
                    address.Local.Y,
                    address.Local.Z);

            if (fluid.Fluid !=
                lava)
            {
                continue;
            }

            foundLava = true;
            break;
        }

        Assert.True(
            foundLava,
            "A strong volcano core must place authored lava above the crater floor.");
    }

    private static (int X, int Z)
        FindVolcanoCore(
            BiomeField field)
    {
        for (var z = -4096;
             z <= 4096;
             z += 32)
        {
            for (var x = -4096;
                 x <= 4096;
                 x += 32)
            {
                var sample =
                    field.Sample(
                        x,
                        z);

                if (string.Equals(
                        sample.Primary,
                        "asteria:overworld/volcano",
                        StringComparison.Ordinal) &&
                    sample.PrimaryTerrainStrength >=
                        0.94f)
                {
                    return (
                        x,
                        z);
                }
            }
        }

        throw new Xunit.Sdk.XunitException(
            "Could not find a deterministic strong volcano core.");
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
