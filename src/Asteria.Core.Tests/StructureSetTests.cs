using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class StructureSetTests
{
    [Fact]
    public void StructureSetJsonParsesOrderedRelativeElements()
    {
        var definition =
            StructureSetDefinitionJson.Parse(
                """
                {
                  "id": "asteria:test_set",
                  "locatable": true,
                  "priority": 4,
                  "conflictGroups": ["natural"],
                  "reserveSpace": true,
                  "elements": [
                    {
                      "id": "root",
                      "structure": "asteria:root",
                      "required": true
                    },
                    {
                      "id": "detail",
                      "structure": "asteria:detail",
                      "count": { "min": 1, "max": 2 },
                      "chance": 0.75,
                      "placement": {
                        "relativeTo": "root",
                        "minDistance": 2,
                        "maxDistance": 6,
                        "minSeparation": 2,
                        "attempts": 12
                      }
                    }
                  ]
                }
                """);

        Assert.Equal(
            "asteria:test_set",
            definition.Id);
        Assert.True(
            definition.Locatable);
        Assert.Equal(
            4,
            definition.Priority);
        Assert.True(
            definition.ReserveSpace);
        Assert.Equal(
            "natural",
            Assert.Single(
                definition.ConflictGroups));
        Assert.Equal(
            2,
            definition.Elements.Count);
        Assert.Equal(
            "root",
            definition.Elements[1]
                .Placement
                .RelativeTo);
        Assert.Equal(
            2,
            definition.Elements[1]
                .Count
                .Max);
    }

    [Fact]
    public void StructureSetMaterializesAllAcceptedPiecesAndQueriesLogicalRoot()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker_a"),
                new BlockDefinition(
                    "asteria:marker_b"),
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
                        "asteria:stone"),
                ]);
        var root =
            new StructureDefinition(
                "asteria:root_piece",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        0,
                        0,
                        0,
                        "asteria:marker_a",
                        BlockOrientation.Y),
                ],
                restrictions:
                    new StructureRestrictionsDefinition(
                        maxSlope: 0,
                        requiresDryGround: true,
                        requiredBiomeCoverage: 1f));
        var detail =
            new StructureDefinition(
                "asteria:detail_piece",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        2,
                        0,
                        0,
                        "asteria:marker_b",
                        BlockOrientation.Y),
                ],
                restrictions:
                    new StructureRestrictionsDefinition(
                        maxSlope: 0,
                        requiresDryGround: true,
                        requiredBiomeCoverage: 1f));
        var structures =
            new StructureRegistry(
            [
                root,
                detail,
            ]);
        var set =
            new StructureSetDefinition(
                "asteria:test_set",
                [
                    new StructureSetElementDefinition(
                        "root",
                        root.Id,
                        required: true),
                    new StructureSetElementDefinition(
                        "detail",
                        detail.Id,
                        required: true),
                ],
                priority: 3,
                conflictGroups:
                [
                    "natural",
                ],
                reserveSpace: true);
        var sets =
            new StructureSetRegistry(
            [
                set,
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
                        set.Id,
                        spacing: 16,
                        chance: 1f,
                        jitter: 0),
                ]);
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

        var placements =
            generator.SurfaceStructuresIntersecting(
                0,
                0,
                Chunk.Size,
                Chunk.Size);

        Assert.Equal(
            2,
            placements.Count);
        Assert.All(
            placements,
            placement =>
            {
                Assert.Equal(
                    set.Id,
                    placement.Reference);
                Assert.Equal(
                    8,
                    placement.PlacementAnchorX);
                Assert.Equal(
                    8,
                    placement.PlacementAnchorZ);
            });

        var nearest =
            generator.FindNearestSurfaceStructure(
                set.Id,
                0,
                0,
                16);
        Assert.True(
            nearest.HasValue);
        Assert.Equal(
            -8,
            nearest.Value.PlacementAnchorX);
        Assert.Equal(
            -8,
            nearest.Value.PlacementAnchorZ);

        var chunk =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));
        Assert.Equal(
            blocks.GetId(
                "asteria:marker_a"),
            chunk.GetBlock(
                8,
                0,
                8));
        Assert.Equal(
            blocks.GetId(
                "asteria:marker_b"),
            chunk.GetBlock(
                10,
                0,
                8));
    }
}
