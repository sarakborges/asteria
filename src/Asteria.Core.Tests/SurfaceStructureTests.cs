using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class SurfaceStructureTests
{
    [Fact]
    public void DefaultStructurePackParsesAndMatchesActiveOverworldRoots()
    {
        var blocks =
            BlockRegistry.FromJson(
                ReadJsonDirectory(
                    "blocks"));
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var dimensions =
            DimensionRegistry.FromJson(
                ReadJsonDirectory(
                    "dimensions"));
        var overworld =
            dimensions.Get(
                DimensionId.Overworld);

        structures.ValidateBlocks(
            blocks);
        dimensions.ValidateStructures(
            structures);

        Assert.Equal(
            8,
            structures.Count);
        Assert.Equal(
            new[]
            {
                "asteria:boulder_big",
                "asteria:boulder_huge",
                "asteria:boulder_medium",
                "asteria:boulder_small",
                "asteria:tree_oak_01",
                "asteria:tree_oak_02",
                "asteria:tree_oak_03",
                "asteria:tree_oak_04",
            },
            structures
                .Definitions()
                .Select(
                    definition =>
                        definition.Id)
                .ToArray());
        Assert.Equal(
            24,
            overworld
                .GeneratedSurfaceStructures
                .Count);
        Assert.All(
            overworld
                .GeneratedSurfaceStructures,
            generated =>
            {
                Assert.Contains(
                    generated.Biome,
                    overworld.SurfaceBiomes);
                Assert.True(
                    structures.ResolvesReference(
                        generated.Structure));
                Assert.DoesNotContain(
                    "enchanted_forest",
                    generated.Biome,
                    StringComparison.Ordinal);
            });

        Assert.Equal(
            10,
            structures
                .Get(
                    "asteria:boulder_small")
                .Voxels
                .Count);
        Assert.Equal(
            168,
            structures
                .Get(
                    "asteria:boulder_huge")
                .Voxels
                .Count);
    }

    [Fact]
    public void OakGroupAuthorsGroundAndGenerationPolicies()
    {
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var oak =
            structures.ResolveReference(
                "asteria:tree_oak");

        Assert.Equal(
            4,
            oak.Count);

        Assert.All(
            oak,
            definition =>
            {
                Assert.Equal(
                    new[]
                    {
                        "asteria:grass_block",
                        "asteria:dirt",
                    },
                    definition.Restrictions
                        .GroundBlocks);
                Assert.Equal(
                    StructureReplacePolicy.Terrain,
                    definition.Generation.ReplacePolicy);
                Assert.Equal(
                    StructureFluidPolicy.Forbid,
                    definition.Generation.FluidPolicy);
                Assert.False(
                    definition.Generation.ReserveSpace);
                Assert.Contains(
                    "tree",
                    definition.ConflictGroups);
            });
    }

    [Fact]
    public void OakGroupMaterializesOnlyOnAllowedGround()
    {
        var blocks =
            BlockRegistry.FromJson(
                ReadJsonDirectory(
                    "blocks"));
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var oakLog =
            blocks.GetId(
                "asteria:log_oak");

        var allowed =
            FlatStructureGenerator(
                blocks,
                structures,
                "asteria:grass_block",
                "asteria:tree_oak");
        var rejected =
            FlatStructureGenerator(
                blocks,
                structures,
                "asteria:stone",
                "asteria:tree_oak");

        Assert.True(
            ChunkContains(
                allowed.Materialize(
                    new ChunkCoord(
                        0,
                        2,
                        0)),
                oakLog));
        Assert.False(
            ChunkContains(
                rejected.Materialize(
                    new ChunkCoord(
                        0,
                        2,
                        0)),
                oakLog));
    }

    [Fact]
    public void HigherPriorityConflictRejectsWholeLowerCandidate()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass_block"),
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker_high"),
                new BlockDefinition(
                    "asteria:marker_low"),
            ]);
        var sharedRestrictions =
            new StructureRestrictionsDefinition(
                maxSlope: 0,
                requiresDryGround: true,
                requiredBiomeCoverage: 1f);
        var structures =
            new StructureRegistry(
            [
                new StructureDefinition(
                    "asteria:high",
                    rotation: false,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:marker_high",
                            BlockOrientation.Y),
                    ],
                    restrictions:
                        sharedRestrictions,
                    priority: 10,
                    conflictGroups:
                    [
                        "test",
                    ]),
                new StructureDefinition(
                    "asteria:low",
                    rotation: false,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:marker_low",
                            BlockOrientation.Y),
                    ],
                    restrictions:
                        sharedRestrictions,
                    priority: 0,
                    conflictGroups:
                    [
                        "test",
                    ]),
            ]);
        var generator =
            FlatStructureGenerator(
                blocks,
                structures,
                "asteria:grass_block",
                "asteria:high",
                "asteria:low");
        var chunk =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));

        Assert.Equal(
            blocks.GetId(
                "asteria:marker_high"),
            chunk.GetBlock(
                8,
                0,
                8));
    }

    [Fact]
    public void TerrainReplacementDoesNotOverwritePreviouslyClaimedStructureVoxel()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass_block"),
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker_any"),
                new BlockDefinition(
                    "asteria:marker_terrain"),
            ]);
        var restrictions =
            new StructureRestrictionsDefinition(
                maxSlope: 0,
                requiresDryGround: true,
                requiredBiomeCoverage: 1f);
        var structures =
            new StructureRegistry(
            [
                new StructureDefinition(
                    "asteria:a_any",
                    rotation: false,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:marker_any",
                            BlockOrientation.Y),
                    ],
                    restrictions:
                        restrictions),
                new StructureDefinition(
                    "asteria:z_terrain",
                    rotation: false,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:marker_terrain",
                            BlockOrientation.Y),
                    ],
                    restrictions:
                        restrictions,
                    generation:
                        new StructureGenerationDefinition(
                            StructureReplacePolicy.Terrain,
                            StructureFluidPolicy.Displace,
                            false)),
            ]);
        var generator =
            FlatStructureGenerator(
                blocks,
                structures,
                "asteria:grass_block",
                "asteria:a_any",
                "asteria:z_terrain");
        var chunk =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));

        Assert.Equal(
            blocks.GetId(
                "asteria:marker_any"),
            chunk.GetBlock(
                8,
                0,
                8));
    }

    [Fact]
    public void BlockTemplateRejectsUnsupportedPaletteCapabilities()
    {
        Assert.Throws<FormatException>(
            () =>
                StructureDefinitionJson.Parse(
                    """
                    {
                      "id":"asteria:test_structure",
                      "rotation":false,
                      "palette":{
                        "S":{
                          "block":"asteria:stone",
                          "objects":[{"object":"asteria:pebble"}]
                        }
                      },
                      "layers":[
                        {"y":0,"rows":["S"]}
                      ]
                    }
                    """));
    }

    [Fact]
    public void RotatingTemplateValidatesEveryProducedBlockOrientation()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:log",
                    orientations:
                    [
                        BlockOrientation.X,
                    ]),
            ]);
        var structures =
            new StructureRegistry(
            [
                new StructureDefinition(
                    "asteria:test_structure",
                    rotation: true,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:log",
                            BlockOrientation.X),
                    ]),
            ]);

        Assert.Throws<ArgumentException>(
            () =>
                structures.ValidateBlocks(
                    blocks));
    }

    [Fact]
    public void StructureCrossesHorizontalAndVerticalChunkBoundariesWithoutClipping()
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
                new BlockDefinition(
                    "asteria:marker"),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/flat",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0f,
                    0f,
                    64,
                    0f,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:grass_block",
                        1),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:dirt",
                        4),
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var structures =
            new StructureRegistry(
            [
                new StructureDefinition(
                    "asteria:test_structure",
                    rotation: false,
                    anchor: default,
                    voxels:
                    [
                        new StructureVoxelDefinition(
                            0,
                            0,
                            0,
                            "asteria:marker",
                            BlockOrientation.Y),
                        new StructureVoxelDefinition(
                            9,
                            1,
                            0,
                            "asteria:marker",
                            BlockOrientation.Y),
                        new StructureVoxelDefinition(
                            0,
                            17,
                            0,
                            "asteria:marker",
                            BlockOrientation.Y),
                    ],
                    restrictions:
                        new StructureRestrictionsDefinition(
                            maxSlope: 0,
                            requiresDryGround: true,
                            requiredBiomeCoverage: 1f)),
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
                generatedSurfaceStructures:
                [
                    new DimensionGeneratedSurfaceStructureDefinition(
                        biome.Id,
                        "asteria:test_structure",
                        spacing: 16,
                        chance: 1f,
                        jitter: 0),
                ]);
        var biomes =
            new BiomeRegistry(
            [
                biome,
            ]);
        var generator =
            new BiomeWorldGenerator(
                77UL,
                dimension,
                blocks,
                new FluidRegistry(
                    Array.Empty<FluidDefinition>()),
                biomes,
                structures);
        var marker =
            blocks.GetId(
                "asteria:marker");

        var leftRange =
            generator.GetSurfaceRange(
                0,
                0);
        var rightRange =
            generator.GetSurfaceRange(
                1,
                0);

        Assert.True(
            leftRange.MaximumWorldY >=
            49);
        Assert.True(
            rightRange.MaximumWorldY >=
            33);

        var leftBase =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));
        var rightBase =
            generator.Materialize(
                new ChunkCoord(
                    1,
                    2,
                    0));
        var leftTop =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    3,
                    0));

        Assert.Equal(
            marker,
            leftBase.GetBlock(
                8,
                0,
                8));
        Assert.Equal(
            marker,
            rightBase.GetBlock(
                1,
                1,
                8));
        Assert.Equal(
            marker,
            leftTop.GetBlock(
                8,
                1,
                8));
    }

    [Fact]
    public void StructureRotationRotatesOffsetsAndHorizontalBlockOrientation()
    {
        Assert.Equal(
            (
                -3,
                2,
                1),
            StructureDefinition.RotateOffset(
                StructureRotation.Degrees90,
                1,
                2,
                3));
        Assert.Equal(
            BlockOrientation.Z,
            StructureDefinition.RotateOrientation(
                StructureRotation.Degrees90,
                BlockOrientation.X));
        Assert.Equal(
            BlockOrientation.Y,
            StructureDefinition.RotateOrientation(
                StructureRotation.Degrees270,
                BlockOrientation.Y));
    } 
    private static BiomeWorldGenerator FlatStructureGenerator(
        BlockRegistry blocks,
        StructureRegistry structures,
        string surfaceBlock,
        params string[] structureReferences)
    {
        var biome =
            new BiomeDefinition(
                "asteria:test/flat",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    0f,
                    0f,
                    64,
                    0f,
                    32),
                surfaceBlock ==
                    "asteria:grass_block"
                    ?
                    [
                        new BiomeSurfaceLayerDefinition(
                            "asteria:grass_block",
                            1),
                        new BiomeSurfaceLayerDefinition(
                            blocks.TryGetId(
                                    "asteria:dirt",
                                    out _)
                                ? "asteria:dirt"
                                : "asteria:stone",
                            4),
                        new BiomeSurfaceLayerDefinition(
                            "asteria:stone"),
                    ]
                    :
                    [
                        new BiomeSurfaceLayerDefinition(
                            surfaceBlock),
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
                generatedSurfaceStructures:
                    structureReferences
                        .Select(reference =>
                            new DimensionGeneratedSurfaceStructureDefinition(
                                biome.Id,
                                reference,
                                spacing: 16,
                                chance: 1f,
                                jitter: 0))
                        .ToArray());

        return new BiomeWorldGenerator(
            77UL,
            dimension,
            blocks,
            new FluidRegistry(
                Array.Empty<FluidDefinition>()),
            new BiomeRegistry(
            [
                biome,
            ]),
            structures);
    }

    private static bool ChunkContains(
        Chunk chunk,
        BlockRuntimeId block)
    {
        var found =
            false;

        chunk.VisitBlockCells(
            (_, _, _, candidate) =>
            {
                if (candidate.Block ==
                    block)
                {
                    found =
                        true;
                }
            });

        return found;
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
