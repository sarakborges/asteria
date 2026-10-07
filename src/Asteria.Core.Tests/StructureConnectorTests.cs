using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class StructureConnectorTests
{
    [Fact]
    public void StructureJsonParsesConnectorOnlyInputMarkers()
    {
        var definition =
            StructureDefinitionJson.Parse(
                """
                {
                  "id": "asteria:connector_child",
                  "rotation": false,
                  "palette": {
                    "I": {
                      "connector": {
                        "face": "left"
                      }
                    },
                    "B": {
                      "block": "asteria:marker"
                    }
                  },
                  "layers": [
                    {
                      "y": 0,
                      "rows": ["IB"]
                    }
                  ]
                }
                """);

        Assert.Single(
            definition.Voxels);
        var connector =
            Assert.Single(
                definition.Connectors);
        Assert.Null(
            connector.Target);
        Assert.Equal(
            StructureConnectorFace.Left,
            connector.Face);
        Assert.Equal(
            0,
            connector.X);
        Assert.Equal(
            0,
            connector.Y);
        Assert.Equal(
            0,
            connector.Z);
    }

    [Fact]
    public void ConnectorChainsExpandBeforeQueryAndMaterialization()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:parent"),
                new BlockDefinition(
                    "asteria:child"),
                new BlockDefinition(
                    "asteria:leaf"),
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

        var parent =
            new StructureDefinition(
                "asteria:parent_piece",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        0,
                        0,
                        0,
                        "asteria:parent",
                        BlockOrientation.Y),
                ],
                connectors:
                [
                    new StructureConnectorDefinition(
                        0,
                        0,
                        0,
                        StructureConnectorFace.Right,
                        "asteria:child_piece",
                        strength: 1f,
                        strengthLossOnEachLoop: 1f,
                        minDistance: 1,
                        maxDistance: 1),
                ]);

        var child =
            new StructureDefinition(
                "asteria:child_piece",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        0,
                        0,
                        0,
                        "asteria:child",
                        BlockOrientation.Y),
                ],
                connectors:
                [
                    new StructureConnectorDefinition(
                        0,
                        0,
                        0,
                        StructureConnectorFace.Left),
                    new StructureConnectorDefinition(
                        0,
                        0,
                        0,
                        StructureConnectorFace.Right,
                        "asteria:leaf_piece",
                        strength: 1f,
                        strengthLossOnEachLoop: 1f,
                        minDistance: 1,
                        maxDistance: 1),
                ]);

        var leaf =
            new StructureDefinition(
                "asteria:leaf_piece",
                rotation: false,
                anchor: default,
                voxels:
                [
                    new StructureVoxelDefinition(
                        0,
                        0,
                        0,
                        "asteria:leaf",
                        BlockOrientation.Y),
                ],
                connectors:
                [
                    new StructureConnectorDefinition(
                        0,
                        0,
                        0,
                        StructureConnectorFace.Left),
                ]);

        var structures =
            new StructureRegistry(
            [
                parent,
                child,
                leaf,
            ]);
        structures.ValidateBlocks(
            blocks);

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
                        parent.Id,
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
                structures);

        var placements =
            generator.SurfaceStructuresIntersecting(
                0,
                0,
                Chunk.Size,
                Chunk.Size);

        Assert.Equal(
            3,
            placements.Count);
        Assert.Equal(
            [
                parent.Id,
                child.Id,
                leaf.Id,
            ],
            placements
                .Select(value =>
                    value.StructureId)
                .ToArray());
        Assert.All(
            placements,
            placement =>
            {
                Assert.Equal(
                    8,
                    placement.PlacementAnchorX);
                Assert.Equal(
                    8,
                    placement.PlacementAnchorZ);
            });

        var chunk =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));
        Assert.Equal(
            blocks.GetId(
                "asteria:parent"),
            chunk.GetBlock(
                8,
                0,
                8));
        Assert.Equal(
            blocks.GetId(
                "asteria:child"),
            chunk.GetBlock(
                9,
                0,
                8));
        Assert.Equal(
            blocks.GetId(
                "asteria:leaf"),
            chunk.GetBlock(
                10,
                0,
                8));
    }
}
