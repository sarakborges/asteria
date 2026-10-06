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

        var biomeDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "packs",
                "default",
                "data",
                "biomes");
        Assert.Equal(
            Directory.EnumerateFiles(
                    biomeDirectory,
                    "*.json")
                .Count(),
            biomes.Count);

        foreach (var biomeId in
                 new[]
                 {
                     "asteria:overworld/alps",
                     "asteria:overworld/arctic",
                     "asteria:overworld/enchanted_forest",
                     "asteria:overworld/gorge",
                     "asteria:overworld/mountain_belt",
                     "asteria:overworld/mountains",
                     "asteria:overworld/ocean",
                     "asteria:overworld/volcano",
                 })
        {
            _ =
                biomes.Get(
                    biomeId);
        }

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
    public void TerrainHeightRemainsContinuousAcrossNegativeNoiseLatticeBoundary()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/noisy",
                new BiomeSurfaceLayoutDefinition(
                    regionMin: 192,
                    regionMax: 192),
                new BiomeTerrainDefinition(
                    baseHeightOffset: 64f,
                    macroAmplitude: 48f,
                    macroScale: 64,
                    detailAmplitude: 0f,
                    detailScale: 32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var generator =
            new BiomeWorldGenerator(
                91,
                TestDimension(
                [
                    biome.Id,
                ]),
                blocks,
                new BiomeRegistry(
                [
                    biome,
                ]));

        var left =
            generator.SurfaceHeight(
                -65,
                17);
        var boundary =
            generator.SurfaceHeight(
                -64,
                17);
        var right =
            generator.SurfaceHeight(
                -63,
                17);

        Assert.InRange(
            Math.Abs(
                boundary -
                left),
            0,
            2);
        Assert.InRange(
            Math.Abs(
                right -
                boundary),
            0,
            2);
    }

    [Fact]
    public void SurfaceMaterialBelongsToPrimaryBiomeNotInfluenceWeight()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass_block"),
                new BlockDefinition(
                    "asteria:sand"),
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var primary =
            FlatBiome(
                "asteria:test/primary",
                "asteria:grass_block",
                "asteria:stone");
        var secondary =
            FlatBiome(
                "asteria:test/secondary",
                "asteria:sand",
                "asteria:stone");
        var materials =
            new BiomeSurfaceMaterialField(
                17,
                [
                    primary,
                    secondary,
                ],
                blocks);
        var sample =
            new BiomeSample(
                primary.Id,
                [
                    new BiomeInfluence(
                        secondary.Id,
                        0.99f),
                    new BiomeInfluence(
                        primary.Id,
                        0.01f),
                ]);

        Assert.Equal(
            blocks.GetId(
                "asteria:grass_block"),
            materials.BlockAt(
                sample,
                120,
                -45,
                depth: 0));
    }

    [Fact]
    public void SurfaceMaterialUsesCumulativeFiniteLayersThenCore()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass_block"),
                new BlockDefinition(
                    "asteria:dirt"),
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/layers",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0,
                    0,
                    64,
                    0,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:grass_block",
                        depth: 1),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:dirt",
                        depth: 4),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var materials =
            new BiomeSurfaceMaterialField(
                21,
                [
                    biome,
                ],
                blocks);
        var sample =
            new BiomeSample(
                biome.Id,
                [
                    new BiomeInfluence(
                        biome.Id,
                        1f),
                ]);

        Assert.Equal(
            blocks.GetId(
                "asteria:grass_block"),
            materials.BlockAt(
                sample,
                0,
                0,
                0));
        Assert.Equal(
            blocks.GetId(
                "asteria:dirt"),
            materials.BlockAt(
                sample,
                0,
                0,
                1));
        Assert.Equal(
            blocks.GetId(
                "asteria:dirt"),
            materials.BlockAt(
                sample,
                0,
                0,
                4));
        Assert.Equal(
            blocks.GetId(
                "asteria:stone"),
            materials.BlockAt(
                sample,
                0,
                0,
                5));
    }

    [Fact]
    public void SurfacePatchIsWorldSpaceAndDoesNotReplaceOutsideRadius()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass_block"),
                new BlockDefinition(
                    "asteria:mud"),
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/patch",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0,
                    0,
                    64,
                    0,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:grass_block",
                        depth: 1,
                        patch:
                            new BiomeSurfacePatchDefinition(
                                spacing: 10,
                                radius: 4,
                                jitter: 0,
                                chance: 1f,
                                blocks:
                                [
                                    "asteria:mud",
                                ])),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var materials =
            new BiomeSurfaceMaterialField(
                33,
                [
                    biome,
                ],
                blocks);
        var sample =
            new BiomeSample(
                biome.Id,
                [
                    new BiomeInfluence(
                        biome.Id,
                        1f),
                ]);

        Assert.Equal(
            blocks.GetId(
                "asteria:mud"),
            materials.BlockAt(
                sample,
                5,
                5,
                0));
        Assert.Equal(
            blocks.GetId(
                "asteria:grass_block"),
            materials.BlockAt(
                sample,
                0,
                0,
                0));
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
                baseHeightOffset: 2f);
        var high =
            TestBiome(
                "asteria:test/high",
                baseHeightOffset: 18f);
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
                expected),
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
    public void SphereShellMaterializesFloorAndRoofAndRejectsNegativeChunks()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:sphere_shell",
                    mining:
                        new BlockMiningDefinition(
                            unbreakable: true),
                    dropsSelf: false),
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            TestBiome(
                "asteria:test/plain",
                baseHeightOffset: 20f);
        var dimension =
            new DimensionDefinition(
                new DimensionId(
                    "asteria:test"),
                [
                    biome.Id,
                ],
                seaLevel: 0,
                gravityStrength: 18f,
                new DimensionSpawnDefinition(
                    0,
                    0),
                new DimensionEnvironmentDefinition(
                    new DimensionColor(0, 0, 0),
                    new DimensionColor(255, 255, 255),
                    1f,
                    new DimensionColor(0, 0, 0),
                    0f),
                new DimensionShellDefinition(
                    "asteria:sphere_shell",
                    floorY: 0,
                    roofY: 12));
        var generator =
            new BiomeWorldGenerator(
                5,
                dimension,
                blocks,
                new BiomeRegistry(
                [
                    biome,
                ]));
        var chunk =
            generator.Materialize(
                ChunkCoord.Zero);
        var shell =
            blocks.GetId(
                "asteria:sphere_shell");
        var stone =
            blocks.GetId(
                "asteria:stone");

        Assert.Equal(
            11,
            generator.SurfaceHeight(
                0,
                0));
        Assert.Equal(
            shell,
            chunk.GetBlock(
                0,
                0,
                0));
        Assert.Equal(
            stone,
            chunk.GetBlock(
                0,
                11,
                0));
        Assert.Equal(
            shell,
            chunk.GetBlock(
                0,
                12,
                0));
        Assert.True(
            chunk.GetCell(
                    0,
                    13,
                    0)
                .IsEmpty);
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                generator.Materialize(
                    new ChunkCoord(
                        0,
                        -1,
                        0)));
    }

    [Fact]
    public void DimensionSeaLevelOffsetsAuthoredBiomeHeight()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            TestBiome(
                "asteria:test/plain",
                baseHeightOffset: 8f);
        var dimension =
            new DimensionDefinition(
                new DimensionId(
                    "asteria:test"),
                [
                    biome.Id,
                ],
                seaLevel: 90,
                gravityStrength: 18f,
                new DimensionSpawnDefinition(
                    0,
                    0),
                new DimensionEnvironmentDefinition(
                    new DimensionColor(0, 0, 0),
                    new DimensionColor(255, 255, 255),
                    1f,
                    new DimensionColor(0, 0, 0),
                    0f));
        var generator =
            new BiomeWorldGenerator(
                5,
                dimension,
                blocks,
                new BiomeRegistry(
                [
                    biome,
                ]));

        Assert.Equal(
            98,
            generator.SurfaceHeight(
                0,
                0));
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
                    baseHeightOffset: 5f,
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
                var chunkX =
                    centerChunk.X +
                    dx;
                var chunkZ =
                    centerChunk.Z +
                    dz;
                var surface =
                    generator.GetSurfaceRange(
                        chunkX,
                        chunkZ);
                var minimumChunkY =
                    VoxelCoordinates.FromWorld(
                            0,
                            surface.MinimumWorldY,
                            0)
                        .Chunk.Y;
                var maximumChunkY =
                    VoxelCoordinates.FromWorld(
                            0,
                            surface.MaximumWorldY +
                            1,
                            0)
                        .Chunk.Y;

                for (var chunkY = minimumChunkY;
                     chunkY <= maximumChunkY;
                     chunkY++)
                {
                    var chunk =
                        generator.Materialize(
                            new ChunkCoord(
                                chunkX,
                                chunkY,
                                chunkZ));
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

    private static BiomeDefinition FlatBiome(
        string id,
        string surface,
        string core) =>
        new(
            id,
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(
                0,
                0,
                64,
                0,
                32),
            [
                new BiomeSurfaceLayerDefinition(
                    surface,
                    depth: 1),
                new BiomeSurfaceLayerDefinition(
                    core),
            ]);

    private static DimensionDefinition TestDimension(
        IEnumerable<string> biomeIds) =>
        new(
            new DimensionId(
                "asteria:test"),
            biomeIds,
            seaLevel: 0,
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
                0f));

    private static BiomeDefinition TestBiome(
        string id,
        float baseHeightOffset = 8f,
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
                baseHeightOffset,
                macroAmplitude: 0f,
                macroScale: 128,
                detailAmplitude: 0f,
                detailScale: 32),
            [
                new BiomeSurfaceLayerDefinition(
                    "asteria:stone"),
            ]);
}
