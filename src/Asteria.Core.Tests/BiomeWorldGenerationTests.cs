using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BiomeWorldGenerationTests
{
    [Fact]
    public void DefaultBiomePackParsesAndValidatesBlockReferences()
    {
        var blocks =
            LoadDefaultBlocks();
        var biomes =
            LoadDefaultBiomes();

        biomes.ValidateBlocks(
            blocks);

        Assert.Equal(
            4,
            biomes.Count);

        var swamp =
            biomes.Get(
                "asteria:overworld/swamp");

        Assert.Contains(
            "asteria:overworld/desert",
            swamp.SurfaceLayout.CannotBorder);
        Assert.Contains(
            swamp.Decorations,
            decoration =>
                decoration.Block ==
                "asteria:mushroom_brown");
        Assert.NotNull(
            swamp.SurfaceLayers[0].Patch);
    }

    [Fact]
    public void BiomeFieldIsIndependentFromRegistryInsertionOrder()
    {
        var definitions =
            StandardBiomeDefinitions();
        var forward =
            new BiomeField(
                77,
                "asteria:test",
                new BiomeRegistry(
                    definitions));
        var reverse =
            new BiomeField(
                77,
                "asteria:test",
                new BiomeRegistry(
                    definitions.Reverse()));

        for (var z = -640;
             z <= 640;
             z += 41)
        {
            for (var x = -640;
                 x <= 640;
                 x += 37)
            {
                var left =
                    forward.Sample(
                        x,
                        z);
                var right =
                    reverse.Sample(
                        x,
                        z);

                Assert.Equal(
                    left.Primary,
                    right.Primary);
                Assert.Equal(
                    left.Influences,
                    right.Influences);
            }
        }
    }

    [Fact]
    public void BiomeInfluencesAreNormalizedAndContainPrimary()
    {
        var field =
            new BiomeField(
                91,
                "asteria:test",
                new BiomeRegistry(
                    StandardBiomeDefinitions()));

        var grid =
            field.SampleGrid(
                -1024,
                -1024,
                width: 64,
                depth: 64,
                step: 32);

        for (var z = 0;
             z < grid.Depth;
             z++)
        {
            for (var x = 0;
                 x < grid.Width;
                 x++)
            {
                var sample =
                    grid[x, z];
                var total =
                    sample.Influences.Sum(
                        influence =>
                            influence.Weight);

                Assert.InRange(
                    total,
                    0.9999f,
                    1.0001f);
                Assert.Contains(
                    sample.Influences,
                    influence =>
                        influence.BiomeId ==
                        sample.Primary);
            }
        }
    }

    [Fact]
    public void CannotBorderIsRespectedBySampledPrimaryBiomes()
    {
        var field =
            new BiomeField(
                123,
                "asteria:test",
                new BiomeRegistry(
                    StandardBiomeDefinitions()));
        var grid =
            field.SampleGrid(
                -2048,
                -2048,
                width: 96,
                depth: 96,
                step: 32);

        for (var z = 0;
             z < grid.Depth;
             z++)
        {
            for (var x = 0;
                 x < grid.Width;
                 x++)
            {
                var sample =
                    grid[x, z];

                if (x + 1 <
                    grid.Width)
                {
                    Assert.True(
                        field.AreCompatible(
                            sample.Primary,
                            grid[
                                x + 1,
                                z].Primary));
                }

                if (z + 1 <
                    grid.Depth)
                {
                    Assert.True(
                        field.AreCompatible(
                            sample.Primary,
                            grid[
                                x,
                                z + 1].Primary));
                }
            }
        }
    }

    [Fact]
    public void TerrainHeightBlendsBiomeInfluencesInsteadOfCuttingAtBoundary()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var low =
            TestBiome(
                "asteria:test/low",
                baseHeight: 2f);
        var high =
            TestBiome(
                "asteria:test/high",
                baseHeight: 18f);
        var biomes =
            new BiomeRegistry(
            [
                low,
                high,
            ]);
        var generator =
            new BiomeWorldGenerator(
                44,
                "asteria:test",
                blocks,
                biomes);

        BiomeSample? blended =
            null;
        var sampleX =
            0;
        var sampleZ =
            0;

        for (var z = -1024;
             z <= 1024 &&
             blended is null;
             z += 8)
        {
            for (var x = -1024;
                 x <= 1024;
                 x += 8)
            {
                var sample =
                    generator.Biomes.Sample(
                        x,
                        z);
                if (sample.Influences.Count >
                    1)
                {
                    blended =
                        sample;
                    sampleX =
                        x;
                    sampleZ =
                        z;
                    break;
                }
            }
        }

        Assert.NotNull(
            blended);

        var expected =
            blended!.Influences.Sum(
                influence =>
                    influence.Weight *
                    (influence.BiomeId ==
                         low.Id
                        ? 2f
                        : 18f));

        Assert.Equal(
            (int)Math.Floor(
                expected +
                0.5f),
            generator.SurfaceHeight(
                sampleX,
                sampleZ));
        Assert.InRange(
            generator.SurfaceHeight(
                sampleX,
                sampleZ),
            3,
            17);
    }

    [Fact]
    public void DecorationUsesAuthoredSurfaceAndIsChunkDeterministic()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:mud"),
                new BlockDefinition(
                    "asteria:mushroom_brown",
                    tags:
                    [
                        BlockPhysicsCapabilities.SupportBelow,
                    ],
                    isCollidable: false),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/swamp",
                new BiomeSurfaceLayoutDefinition(
                    regionMin: 128,
                    regionMax: 128),
                new BiomeTerrainDefinition(
                    baseHeight: 5f,
                    macroAmplitude: 0f,
                    macroScale: 128,
                    detailAmplitude: 0f,
                    detailScale: 32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:mud",
                        depth: 1),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ],
                [
                    new BiomeDecorationDefinition(
                        "asteria:mushroom_brown",
                        chance: 1f,
                        surfaceBlocks:
                        [
                            "asteria:mud",
                        ]),
                ]);
        var generator =
            new BiomeWorldGenerator(
                1,
                "asteria:test",
                blocks,
                new BiomeRegistry(
                [
                    biome,
                ]));

        var first =
            generator.Materialize(
                ChunkCoord.Zero);
        var second =
            generator.Materialize(
                ChunkCoord.Zero);
        var mud =
            blocks.GetId(
                "asteria:mud");
        var mushroom =
            blocks.GetId(
                "asteria:mushroom_brown");

        for (var z = 0;
             z < Chunk.Size;
             z++)
        {
            for (var x = 0;
                 x < Chunk.Size;
                 x++)
            {
                Assert.Equal(
                    mud,
                    first.GetBlock(
                        x,
                        5,
                        z));
                Assert.Equal(
                    mushroom,
                    first.GetBlock(
                        x,
                        6,
                        z));
                Assert.Equal(
                    first.GetCell(
                        x,
                        6,
                        z),
                    second.GetCell(
                        x,
                        6,
                        z));
            }
        }
    }

    [Fact]
    public void DefaultWorldGeneratorProducesGroundPlantsFromBiomeDecorators()
    {
        var blocks =
            LoadDefaultBlocks();
        var biomes =
            LoadDefaultBiomes();
        var generator =
            new BiomeWorldGenerator(
                0xA57E_2026UL,
                "asteria:overworld",
                blocks,
                biomes);
        var grass =
            blocks.GetId(
                "asteria:grass");
        var mushroom =
            blocks.GetId(
                "asteria:mushroom_brown");
        var foundGrass =
            false;
        var foundMushroom =
            false;

        for (var chunkZ = -8;
             chunkZ <= 8 &&
             !(foundGrass &&
               foundMushroom);
             chunkZ++)
        {
            for (var chunkX = -8;
                 chunkX <= 8 &&
                 !(foundGrass &&
                   foundMushroom);
                 chunkX++)
            {
                var chunk =
                    generator.Materialize(
                        new ChunkCoord(
                            chunkX,
                            0,
                            chunkZ));

                chunk.VisitBlockCells(
                    (_, _, _, cell) =>
                    {
                        foundGrass |=
                            cell.Block ==
                            grass;
                        foundMushroom |=
                            cell.Block ==
                            mushroom;
                    });
            }
        }

        Assert.True(
            foundGrass);
        Assert.True(
            foundMushroom);
    }

    private static BlockRegistry LoadDefaultBlocks()
    {
        var directory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "blocks");

        return BlockRegistry.FromJson(
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

    private static BiomeDefinition[] StandardBiomeDefinitions() =>
    [
        TestBiome(
            "asteria:test/a",
            cannotBorder:
            [
                "asteria:test/b",
            ]),
        TestBiome(
            "asteria:test/b",
            cannotBorder:
            [
                "asteria:test/a",
            ]),
        TestBiome(
            "asteria:test/c"),
        TestBiome(
            "asteria:test/d",
            weight: 0.6f),
    ];

    private static BiomeDefinition TestBiome(
        string id,
        float baseHeight = 8f,
        float weight = 1f,
        IEnumerable<string>? cannotBorder = null) =>
        new(
            id,
            new BiomeSurfaceLayoutDefinition(
                weight,
                regionMin: 192,
                regionMax: 384,
                cannotBorder),
            new BiomeTerrainDefinition(
                baseHeight,
                macroAmplitude: 0f,
                macroScale: 128,
                detailAmplitude: 0f,
                detailScale: 32),
            [
                new BiomeSurfaceLayerDefinition(
                    "asteria:stone"),
            ]);
}
