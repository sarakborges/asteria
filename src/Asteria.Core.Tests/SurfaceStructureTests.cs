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
        var fluids =
            FluidRegistry.FromJson(
                ReadJsonDirectory(
                    "fluids"));
        var dimensions =
            DimensionRegistry.FromJson(
                ReadJsonDirectory(
                    "dimensions"));
        var overworld =
            dimensions.Get(
                DimensionId.Overworld);

        structures.ValidateBlocks(
            blocks);
        structures.ValidateFluids(
            fluids);
        var structureSets =
            StructureSetRegistry.FromJson(
                ReadJsonDirectory("structure_sets"));
        dimensions.ValidateStructures(
            structures, structureSets);

        Assert.Equal(
            48,
            structures.Count);
        Assert.Equal(3, structures.ResolveReference("asteria:bush_oak").Count);
        Assert.Equal(3, structureSets.Count);
        Assert.Equal(4, structures.ResolveReference("asteria:fallen_log_oak").Count);
        Assert.Equal(3, structures.ResolveReference("asteria:oak_stump").Count);
        Assert.Equal("asteria:boulder_small",
            structureSets.Get("asteria:rock_cluster").Elements[0].Structure);
        Assert.Equal("asteria:bush_oak",
            structureSets.Get("asteria:thicket_oak").Elements[0].Structure);
        Assert.Equal("asteria:tree_oak",
            structureSets.Get("asteria:oak_grove").Elements[0].Structure);
        Assert.Equal(
            10,
            structures.ResolveReference(
                "asteria:river_segment")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:lake")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:mountain_pond")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:mountain_waterfall")
                .Count);
        Assert.Equal(
            4,
            structures.ResolveReference(
                "asteria:river_lake")
                .Count);
        Assert.True(
            structures.ResolvesReference(
                "asteria:river_ocean_mouth"));
        Assert.Equal(
            37,
            overworld
                .GeneratedSurfaceStructures
                .Count);
        var plainsRoots = overworld.GeneratedSurfaceStructures
            .Where(root => root.Biome == "asteria:overworld/plains")
            .ToArray();
        Assert.Equal(13, plainsRoots.Length);
        Assert.All(plainsRoots.Where(root => root.Structure != "asteria:lake"),
            root => Assert.NotNull(root.HabitatWeights));
        Assert.Equal(2.5f, Assert.Single(plainsRoots,
            root => root.Structure == "asteria:rock_cluster")
            .HabitatWeights!.For("rocky"));
        Assert.Equal(0f, Assert.Single(plainsRoots,
            root => root.Structure == "asteria:oak_grove")
            .HabitatWeights!.For("rocky"));
        Assert.All(
            overworld
                .GeneratedSurfaceStructures,
            generated =>
            {
                Assert.Contains(
                    generated.Biome,
                    overworld.SurfaceBiomes);
                Assert.True(
                    structures.ResolvesReference(generated.Structure) ||
                    structureSets.ResolvesReference(generated.Structure));
                Assert.DoesNotContain(
                    "enchanted_forest",
                    generated.Biome,
                    StringComparison.Ordinal);
            });

        foreach (var reference in new[] { "asteria:fallen_log_oak", "asteria:oak_stump" })
        {
            Assert.All(structures.ResolveReference(reference), detail =>
            {
                Assert.True(detail.Rotation);
                Assert.Equal(0, detail.GroundAnchorY);
                Assert.Equal(0, detail.Restrictions.MaxSlope);
                Assert.Equal(1f, detail.Restrictions.RequiredBiomeCoverage);
                Assert.True(detail.Restrictions.RequiresDryGround);
                Assert.Equal(StructureFluidPolicy.Forbid, detail.Generation.FluidPolicy);
                Assert.True(detail.Voxels.All(voxel => voxel.Y >= 1));
                Assert.Contains("asteria:gravel", detail.Restrictions.GroundBlocks);
            });
        }

        Assert.All(
            structures.ResolveReference("asteria:bush_oak"),
            bush =>
            {
                Assert.True(bush.Restrictions.RequiresDryGround);
                Assert.Equal(1f, bush.Restrictions.RequiredBiomeCoverage);
                Assert.Equal(StructureFluidPolicy.Forbid, bush.Generation.FluidPolicy);
                Assert.False(bush.Generation.ReserveSpace);
                Assert.Contains("asteria:leaf_oak", bush.Voxels.Select(v => v.Block));
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
    public void WillowGroupAuthorsBoundedWaterProximity()
    {
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var willow =
            structures.ResolveReference(
                "asteria:tree_willow");

        Assert.Equal(
            3,
            willow.Count);

        Assert.All(
            willow,
            definition =>
            {
                Assert.Equal(
                    new[]
                    {
                        "asteria:grass_block",
                        "asteria:dirt",
                        "asteria:mud",
                    },
                    definition.Restrictions
                        .GroundBlocks);
                var proximity =
                    Assert.Single(
                        definition.Restrictions
                            .Proximity);
                Assert.Equal(
                    StructureProximityMode.Required,
                    proximity.Mode);
                Assert.Equal(
                    "asteria:water",
                    proximity.Target.Fluid);
                Assert.Null(
                    proximity.Target.Block);
                Assert.Equal(
                    1,
                    proximity.MinDistance);
                Assert.Equal(
                    12,
                    proximity.MaxDistance);
                Assert.Equal(
                    StructureReplacePolicy.Terrain,
                    definition.Generation.ReplacePolicy);
                Assert.Equal(
                    StructureFluidPolicy.Forbid,
                    definition.Generation.FluidPolicy);
                Assert.Contains(
                    "tree",
                    definition.ConflictGroups);
            });
    }

    [Fact]
    public void RequiredFluidProximityUsesActualGeneratedFluid()
    {
        var accepted =
            FluidProximityGenerator(
                "asteria:water",
                out var blocks);
        var rejected =
            FluidProximityGenerator(
                "asteria:lava",
                out _);
        var marker =
            blocks.GetId(
                "asteria:marker");
        var coord =
            new ChunkCoord(
                0,
                1,
                0);

        Assert.True(
            ChunkContains(
                accepted.Materialize(
                    coord),
                marker));
        Assert.False(
            ChunkContains(
                rejected.Materialize(
                    coord),
                marker));
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
    public void StructureQueriesReturnTheSameAcceptedPlacementUsedByMaterialization()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker"),
            ]);
        var structure =
            new StructureDefinition(
                "asteria:test_marker",
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
                ],
                restrictions:
                    new StructureRestrictionsDefinition(
                        maxSlope: 0,
                        requiresDryGround: true,
                        requiredBiomeCoverage: 1f));
        var generator =
            FlatStructureGenerator(
                blocks,
                new StructureRegistry(
                [
                    structure,
                ]),
                "asteria:stone",
                structure.Id);

        var area =
            generator.SurfaceStructuresIntersecting(
                0,
                0,
                Chunk.Size,
                Chunk.Size);
        var placement =
            Assert.Single(
                area);
        var nearest =
            generator.FindNearestSurfaceStructure(
                structure.Id,
                0,
                0,
                64);

        Assert.True(
            nearest.HasValue);
        Assert.Equal(
            -8,
            nearest.Value.PlacementAnchorX);
        Assert.Equal(
            -8,
            nearest.Value.PlacementAnchorZ);
        Assert.Equal(
            -8,
            nearest.Value.AnchorX);
        Assert.Equal(
            -8,
            nearest.Value.AnchorZ);
        Assert.Equal(
            8,
            placement.AnchorX);
        Assert.Equal(
            8,
            placement.AnchorZ);

        var chunk =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    placement.AnchorY /
                    Chunk.Size,
                    0));
        Assert.Equal(
            blocks.GetId(
                "asteria:marker"),
            chunk.GetBlock(
                placement.AnchorX,
                placement.AnchorY %
                Chunk.Size,
                placement.AnchorZ));
    }

    [Fact]
    public void StructureSearchIsBoundedByReferenceAndDistance()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker"),
            ]);
        var structure =
            new StructureDefinition(
                "asteria:test_marker",
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
                ]);
        var generator =
            FlatStructureGenerator(
                blocks,
                new StructureRegistry(
                [
                    structure,
                ]),
                "asteria:stone",
                structure.Id);

        Assert.Null(
            generator.FindNearestSurfaceStructure(
                "asteria:missing",
                0,
                0,
                64));
        Assert.Null(
            generator.FindNearestSurfaceStructure(
                structure.Id,
                0,
                0,
                4));
        Assert.NotNull(
            generator.FindNearestSurfaceStructure(
                structure.Id,
                0,
                0,
                12));
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
    private static BiomeWorldGenerator FluidProximityGenerator(
        string targetFluid,
        out BlockRegistry blocks)
    {
        blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker"),
            ]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(
                        0,
                        96,
                        192),
                    0.7f),
                new FluidDefinition(
                    "asteria:lava",
                    new FluidColor(
                        255,
                        96,
                        0),
                    0.9f),
            ]);
        var biome =
            new BiomeDefinition(
                "asteria:test/ocean",
                new BiomeSurfaceLayoutDefinition(),
                new BiomeTerrainDefinition(
                    -4f,
                    0f,
                    64,
                    0f,
                    32),
                [
                    new BiomeSurfaceLayerDefinition(
                        "asteria:stone"),
                ]);
        var structure =
            new StructureDefinition(
                "asteria:test_marker",
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
                ],
                restrictions:
                    new StructureRestrictionsDefinition(
                        maxSlope: 0,
                        requiresDryGround: false,
                        requiredBiomeCoverage: 1f,
                        proximity:
                        [
                            new StructureProximityRestrictionDefinition(
                                new StructureProximityTargetDefinition(
                                    fluid:
                                        targetFluid),
                                StructureProximityMode.Required,
                                maxDistance: 2,
                                minDistance: 1),
                        ]));
        var structures =
            new StructureRegistry(
            [
                structure,
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
                generatedOcean:
                    new DimensionGeneratedOceanDefinition(
                        biome.Id,
                        "asteria:water"),
                generatedSurfaceStructures:
                [
                    new DimensionGeneratedSurfaceStructureDefinition(
                        biome.Id,
                        structure.Id,
                        spacing: 16,
                        chance: 1f,
                        jitter: 0),
                ]);

        return new BiomeWorldGenerator(
            91UL,
            dimension,
            blocks,
            fluids,
            new BiomeRegistry(
            [
                biome,
            ]),
            structures);
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
            FluidRegistry.FromJson(
                ReadJsonDirectory(
                    "fluids")),
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
