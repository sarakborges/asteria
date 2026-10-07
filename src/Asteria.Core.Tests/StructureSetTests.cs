using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class StructureSetTests
{
    [Fact]
    public void DefinitionRejectsForwardRelativeReference()
    {
        Assert.Throws<ArgumentException>(
            () =>
                new StructureSetDefinition(
                    "asteria:test_set",
                    elements:
                    [
                        new StructureSetElementDefinition(
                            "first",
                            "asteria:marker",
                            placement:
                                new StructureSetElementPlacementDefinition(
                                    relativeTo:
                                        "second")),
                        new StructureSetElementDefinition(
                            "second",
                            "asteria:marker"),
                    ]));
    }

    [Fact]
    public void OmittedCountDefaultsToExactlyOne()
    {
        var definition =
            StructureSetDefinitionJson.Parse(
                """
                {
                  "id":"asteria:test_set",
                  "elements":[
                    {
                      "id":"root",
                      "structure":"asteria:marker"
                    }
                  ]
                }
                """);
        var element =
            Assert.Single(
                definition.Elements);

        Assert.Equal(
            1,
            element.Count.Minimum);
        Assert.Equal(
            1,
            element.Count.Maximum);
    }

    [Fact]
    public void RegistryRejectsMissingStructureReference()
    {
        var sets =
            new StructureSetRegistry(
            [
                new StructureSetDefinition(
                    "asteria:test_set",
                    elements:
                    [
                        new StructureSetElementDefinition(
                            "root",
                            "asteria:missing"),
                    ]),
            ]);

        Assert.Throws<ArgumentException>(
            () =>
                sets.ValidateStructures(
                    StructureRegistry.Empty));
    }

    [Fact]
    public void MultiPieceSetMaterializesAsOneDeterministicRoot()
    {
        var fixture =
            CreateFixture(
                includeHighPriorityConflict:
                    false);
        var chunk =
            fixture.Generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));
        var top =
            fixture.Generator.Materialize(
                new ChunkCoord(
                    0,
                    3,
                    0));

        var range =
            fixture.Generator.GetSurfaceRange(
                0,
                0);

        Assert.Equal(
            fixture.Center,
            chunk.GetBlock(
                8,
                0,
                8));
        Assert.Equal(
            fixture.Satellite,
            chunk.GetBlock(
                10,
                0,
                8));
        Assert.Equal(
            fixture.Center,
            top.GetBlock(
                8,
                1,
                8));

        Assert.True(
            range.MaximumWorldY >=
            49);

        var repeated =
            CreateFixture(
                includeHighPriorityConflict:
                    false);
        var repeatedChunk =
            repeated.Generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));

        Assert.Equal(
            chunk.GetCell(
                8,
                0,
                8),
            repeatedChunk.GetCell(
                8,
                0,
                8));
        Assert.Equal(
            chunk.GetCell(
                10,
                0,
                8),
            repeatedChunk.GetCell(
                10,
                0,
                8));
    }

    [Fact]
    public void HigherPriorityConflictRejectsWholeSet()
    {
        var fixture =
            CreateFixture(
                includeHighPriorityConflict:
                    true);
        var chunk =
            fixture.Generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));

        Assert.Equal(
            fixture.High,
            chunk.GetBlock(
                8,
                0,
                8));
        Assert.NotEqual(
            fixture.Satellite,
            chunk.GetBlock(
                10,
                0,
                8));
    }

    [Fact]
    public void ParserRejectsUnsupportedMetadata()
    {
        Assert.Throws<FormatException>(
            () =>
                StructureSetDefinitionJson.Parse(
                    """
                    {
                      "id":"asteria:test_set",
                      "locatable":true,
                      "elements":[
                        {
                          "id":"root",
                          "structure":"asteria:marker"
                        }
                      ]
                    }
                    """));
    }

    private static Fixture CreateFixture(
        bool includeHighPriorityConflict)
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:grass_block"),
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:center"),
                new BlockDefinition(
                    "asteria:satellite"),
                new BlockDefinition(
                    "asteria:high"),
            ]);
        var restrictions =
            new StructureRestrictionsDefinition(
                maxSlope: 0,
                requiresDryGround: true,
                requiredBiomeCoverage: 1f);
        var center =
            new StructureDefinition(
                "asteria:center_structure",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        0,
                        0,
                        0,
                        "asteria:center",
                        BlockOrientation.Y),
                    new StructureVoxelDefinition(
                        0,
                        17,
                        0,
                        "asteria:center",
                        BlockOrientation.Y),
                ],
                restrictions:
                    restrictions,
                conflictGroups:
                [
                    "landmark",
                ]);
        var satellite =
            new StructureDefinition(
                "asteria:satellite_structure",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        2,
                        0,
                        0,
                        "asteria:satellite",
                        BlockOrientation.Y),
                ],
                restrictions:
                    restrictions,
                conflictGroups:
                [
                    "landmark",
                ]);
        var high =
            new StructureDefinition(
                "asteria:high_structure",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        0,
                        0,
                        0,
                        "asteria:high",
                        BlockOrientation.Y),
                ],
                restrictions:
                    restrictions,
                priority: 100,
                conflictGroups:
                [
                    "landmark",
                ]);
        var structures =
            new StructureRegistry(
                includeHighPriorityConflict
                    ?
                    [
                        center,
                        satellite,
                        high,
                    ]
                    :
                    [
                        center,
                        satellite,
                    ]);
        var set =
            new StructureSetDefinition(
                "asteria:test_set",
                priority: 10,
                conflictGroups:
                [
                    "landmark",
                ],
                reserveSpace: true,
                elements:
                [
                    new StructureSetElementDefinition(
                        "center",
                        center.Id,
                        required: true,
                        placement:
                            new StructureSetElementPlacementDefinition(
                                relativeTo:
                                    "origin",
                                attempts: 1)),
                    new StructureSetElementDefinition(
                        "satellite",
                        satellite.Id,
                        required: true,
                        placement:
                            new StructureSetElementPlacementDefinition(
                                relativeTo:
                                    "center",
                                attempts: 1,
                                allowOverlap: true)),
                ]);
        var sets =
            new StructureSetRegistry(
            [
                set,
            ]);
        sets.ValidateStructures(
            structures);

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
                        "asteria:stone"),
                ]);
        var generated =
            new List<DimensionGeneratedSurfaceStructureDefinition>
            {
                new(
                    biome.Id,
                    set.Id,
                    spacing: 16,
                    chance: 1f,
                    jitter: 0),
            };

        if (includeHighPriorityConflict)
        {
            generated.Add(
                new DimensionGeneratedSurfaceStructureDefinition(
                    biome.Id,
                    high.Id,
                    spacing: 16,
                    chance: 1f,
                    jitter: 0));
        }

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
                    generated);
        var dimensions =
            new DimensionRegistry(
            [
                dimension,
            ]);

        dimensions.ValidateStructures(
            structures,
            sets);

        var generator =
            new BiomeWorldGenerator(
                77UL,
                dimension,
                blocks,
                new FluidRegistry(
                    Array.Empty<FluidDefinition>()),
                new BiomeRegistry(
                [
                    biome,
                ]),
                structures,
                sets);

        return new Fixture(
            generator,
            blocks.GetId(
                "asteria:center"),
            blocks.GetId(
                "asteria:satellite"),
            blocks.GetId(
                "asteria:high"));
    }

    private sealed record Fixture(
        BiomeWorldGenerator Generator,
        BlockRuntimeId Center,
        BlockRuntimeId Satellite,
        BlockRuntimeId High);
}
