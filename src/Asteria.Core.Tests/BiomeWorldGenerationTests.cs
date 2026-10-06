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
            7,
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
                TestDimension(
                    definitions.Select(
                        definition =>
                            definition.Id)),
                new BiomeRegistry(
                    definitions));
        var reverse =
            new BiomeField(
                77,
                TestDimension(
                    definitions.Select(
                        definition =>
                            definition.Id)),
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
                    left.Influences.ToArray(),
                    right.Influences.ToArray());
            }
        }
    }

    [Fact]
    public void BiomeInfluencesAreNormalizedAndContainPrimary()
    {
        var field =
            new BiomeField(
                91,
                TestDimension(
                    StandardBiomeDefinitions()
                        .Select(
                            definition =>
                                definition.Id)),
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
                TestDimension(
                    StandardBiomeDefinitions()
                        .Select(
                            definition =>
                                definition.Id)),
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
                TestDimension(
                [
                    low.Id,
                    high.Id,
                ]),
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
                        1 &&
                    sample.Influences
                        .Skip(1)
                        .Any(influence =>
                            influence.Weight >=
                            0.1f))
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
                TestDimension(
                [
                    biome.Id,
                ]),
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
        var dimensions =
            LoadDefaultDimensions();
        dimensions.ValidateBiomes(
            biomes);
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
                biomes);
        var grass =
            blocks.GetId(
                "asteria:grass");
        var mushroom =
            blocks.GetId(
                "asteria:mushroom_brown");
        var plains =
            FindBiomeInterior(
                generator.Biomes,
                "asteria:overworld/plains");
        var swamp =
            FindBiomeInterior(
                generator.Biomes,
                "asteria:overworld/swamp");

        Assert.True(
            HasBlockNear(
                generator,
                plains,
                grass));
        Assert.True(
            HasBlockNear(
                generator,
                swamp,
                mushroom));
    }

    private static (int X, int Z) FindBiomeInterior(
        BiomeField field,
        string biomeId)
    {
        for (var z = -4096;
             z <= 4096;
             z += 64)
        {
            for (var x = -4096;
                 x <= 4096;
                 x += 64)
            {
                var sample =
                    field.Sample(
                        x,
                        z);

                if (sample.Primary ==
                        biomeId &&
                    sample.PrimaryWeight >=
                        0.9f)
                {
                    return (
                        x,
                        z);
                }
            }
        }

        throw new Xunit.Sdk.XunitException(
            $"Could not find an interior sample for {biomeId}.");
    }

    private static bool HasBlockNear(
        BiomeWorldGenerator generator,
        (int X, int Z) center,
        BlockRuntimeId target)
    {
        var centerChunk =
            VoxelCoordinates
                .FromWorld(
                    center.X,
                    0,
                    center.Z)
                .Chunk;

        for (var dz = -1;
             dz <= 1;
             dz++)
        {
            for (var dx = -1;
                 dx <= 1;
                 dx++)
            {
                var chunk =
                    generator.Materialize(
                        new ChunkCoord(
                            centerChunk.X +
                            dx,
                            0,
                            centerChunk.Z +
                            dz));
                var found =
                    false;

                chunk.VisitBlockCells(
                    (_, _, _, cell) =>
                    {
                        found |=
                            cell.Block ==
                            target;
                    });

                if (found)
                {
                    return true;
                }
            }
        }

        return false;
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

    private static DimensionDefinition TestDimension(
        IEnumerable<string> biomeIds) =>
        new(
            new DimensionId(
                "asteria:test"),
            biomeIds,
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
                ambientEnergy: 1f,
                new DimensionColor(
                    0,
                    0,
                    0),
                fogDensity: 0f));

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
                cannotBorder:
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
