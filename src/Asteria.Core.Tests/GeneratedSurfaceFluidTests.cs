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
        Assert.Empty(swamp.SurfaceTerrain.Modifiers);
        Assert.False(swamp.SurfaceTerrain.FillToSeaLevel);
        Assert.Equal(0f,
            Assert.IsType<BiomeRollingTerrainShapeDefinition>(
                swamp.SurfaceTerrain.Shape).Amplitude);
        var mosaic = Assert.IsType<BiomeSurfaceMosaicDefinition>(
            swamp.Palette.SurfaceMosaic);
        Assert.Equal(4, mosaic.Entries.Count);
        Assert.Contains(mosaic.Entries, entry => entry.Fluid == "asteria:water");
        Assert.All(swamp.Palette.Default, layer =>
            Assert.Null(layer.Patch));

        var scorched = biomes.Get("asteria:overworld/scorched_wastes");
        Assert.IsType<BiomeRollingTerrainShapeDefinition>(
            scorched.SurfaceTerrain!.Shape);
        Assert.False(scorched.SurfaceTerrain.FillToSeaLevel);
    }

    [Fact]
    public void LegacyConeAndDetachedCraterRecipesAreRejected()
    {
        Assert.Throws<FormatException>(() => BiomeDefinitionJson.Parse(
            """
            {
              "id": "asteria:test/obsolete",
              "surfaceLayout": {},
              "surfaceTerrain": {
                "type": "cone",
                "baseHeight": 12,
                "height": 72
              },
              "palette":{"default":[{"block":"asteria:stone"}]}
            }
            """));
        Assert.Throws<FormatException>(() => BiomeDefinitionJson.Parse(
            """
            {
              "id": "asteria:test/obsolete",
              "surfaceLayout": {},
              "surfaceTerrain": {
                "type": "rolling",
                "baseHeight": 12,
                "amplitude": 0,
                "scale": 0.01,
                "detailAmplitude": 0,
                "detailScale": 0.03,
                "crater": {"depth": 38}
              },
              "palette":{"default":[{"block":"asteria:stone"}]}
            }
            """));
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
    public void DefaultSwampMosaicPlacesFourMaterialsAtTheSameSurfaceLevel()
    {
        var biomes = BiomeRegistry.FromJson(ReadJsonDirectory("biomes"));
        var blocks = BlockRegistry.FromJson(ReadJsonDirectory("blocks"));
        var fluids = FluidRegistry.FromJson(ReadJsonDirectory("fluids"));
        var overworld = DimensionRegistry.FromJson(
            ReadJsonDirectory("dimensions")).Get(DimensionId.Overworld);
        var swamp = biomes.Get("asteria:overworld/swamp");
        var dimension = new DimensionDefinition(
            overworld.Id, [swamp.Id], overworld.SeaLevel,
            overworld.GravityStrength, overworld.Spawn, overworld.Environment);
        const ulong seed = 91UL;
        var mosaic = new SurfaceMosaicField(seed, [swamp], blocks, fluids);
        var generator = new BiomeWorldGenerator(
            seed, dimension, blocks, fluids, biomes);
        var sample = new BiomeSample(swamp.Id,
            [new BiomeInfluence(swamp.Id, 1f, 1f)]);
        var positions = new Dictionary<string, (int X, int Z)>(
            StringComparer.Ordinal);
        for (var z = -192; z <= 192; z += 4)
        for (var x = -192; x <= 192; x += 4)
        {
            var choice = mosaic.Sample(sample, x, z);
            var key = choice.IsFluid ? "asteria:water" :
                blocks.GetDefinition(choice.Block).Id;
            positions.TryAdd(key, (x, z));
        }

        var expected = new[]
        {
            "asteria:block_grass", "asteria:dirt",
            "asteria:mud", "asteria:water",
        };
        foreach (var id in expected)
        {
            Assert.True(positions.TryGetValue(id, out var pos),
                $"Shared noise must produce {id}.");
            var (chunkCoord, local) = VoxelCoordinates.FromWorld(
                pos.X, dimension.SeaLevel, pos.Z);
            var chunk = generator.Materialize(chunkCoord);
            var cell = chunk.GetCell(local.X, local.Y, local.Z);
            var fluid = chunk.GetFluid(local.X, local.Y, local.Z);
            if (id == "asteria:water")
            {
                Assert.True(cell.IsEmpty);
                Assert.Equal(fluids.GetId(id), fluid.Fluid);
                Assert.Equal(dimension.SeaLevel - 1,
                    generator.SurfaceHeight(pos.X, pos.Z));
                var (groundChunk, groundLocal) = VoxelCoordinates.FromWorld(
                    pos.X, dimension.SeaLevel - 1, pos.Z);
                Assert.Equal(blocks.GetId("asteria:mud"),
                    generator.Materialize(groundChunk).GetCell(
                        groundLocal.X, groundLocal.Y, groundLocal.Z).Block);
            }
            else
            {
                Assert.Equal(blocks.GetId(id), cell.Block);
                Assert.True(fluid.IsEmpty);
                Assert.Equal(dimension.SeaLevel,
                    generator.SurfaceHeight(pos.X, pos.Z));
            }
        }
    }

    [Fact]
    public void SurfaceMosaicValidatesChoiceTypesAndWeights()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeSurfaceMosaicEntryDefinition("asteria:dirt",
                "asteria:water", 1f));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeSurfaceMosaicEntryDefinition(null,
                "asteria:water", 0f));
        Assert.ThrowsAny<ArgumentException>(() =>
            new BiomeSurfaceMosaicDefinition(1, 8, 0.2f, [
                new("asteria:mud", null, 1),
                new(null, "asteria:water", 1),
            ]));
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
                ["asteria:overworld/scorched_wastes"] =
                    typeof(BiomeRollingTerrainShapeDefinition),
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
            BiomeRidgeDetailMode.Modulated,
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
