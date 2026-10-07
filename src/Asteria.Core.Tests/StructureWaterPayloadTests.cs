using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class StructureWaterPayloadTests
{
    [Fact]
    public void StructureJsonParsesWaterPayloadAndTerrainFitFields()
    {
        var definition =
            StructureDefinitionJson.Parse(
                """
                {
                  "id": "asteria:test_water",
                  "locatable": false,
                  "rotation": true,
                  "restrictions": {
                    "minSlope": 2,
                    "maxSlope": 6,
                    "requiresDryGround": false
                  },
                  "generation": {
                    "replacePolicy": "terrain",
                    "fluidPolicy": "displace",
                    "reserveSpace": true
                  },
                  "anchor": { "x": 1, "y": 0, "z": 0 },
                  "groundAnchorY": -1,
                  "clearAbove": 3,
                  "palette": {
                    "W": { "fluid": "asteria:water" },
                    "C": { "clear": true },
                    "I": { "connector": { "face": "back" } }
                  },
                  "layers": [
                    { "y": -1, "rows": ["WC."] },
                    { "y": 0, "rows": [".I."] }
                  ]
                }
                """);

        Assert.False(
            definition.Locatable);
        Assert.Equal(
            -1,
            definition.GroundAnchorY);
        Assert.Equal(
            3,
            definition.ClearAbove);
        Assert.Equal(
            2,
            definition.Restrictions.MinSlope);
        Assert.Equal(
            6,
            definition.Restrictions.MaxSlope);
        Assert.Single(
            definition.FluidVoxels);
        Assert.Single(
            definition.ClearVoxels);
        Assert.Single(
            definition.Connectors);
    }

    [Fact]
    public void StructureFluidAndClearPayloadsReplaceGeneratedTerrain()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var water =
            new FluidDefinition(
                "asteria:water",
                new FluidColor(
                    64,
                    128,
                    255),
                0.7f);
        var fluids =
            new FluidRegistry(
            [
                water,
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
        var structure =
            new StructureDefinition(
                "asteria:test_water",
                rotation: false,
                anchor: default,
                voxels:
                    Array.Empty<StructureVoxelDefinition>(),
                restrictions:
                    new StructureRestrictionsDefinition(
                        maxSlope: 0,
                        requiresDryGround: false),
                generation:
                    new StructureGenerationDefinition(
                        StructureReplacePolicy.Terrain,
                        StructureFluidPolicy.Displace,
                        false),
                fluidVoxels:
                [
                    new StructureFluidVoxelDefinition(
                        0,
                        0,
                        0,
                        water.Id),
                ],
                clearVoxels:
                [
                    new StructureClearVoxelDefinition(
                        1,
                        0,
                        0),
                ],
                groundAnchorY: 0,
                clearAbove: 1);
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
                        structure.Id,
                        spacing: 16,
                        chance: 1f,
                        jitter: 0),
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
                ]),
                new StructureRegistry(
                [
                    structure,
                ]));

        var placement =
            Assert.Single(
                generator.SurfaceStructuresIntersecting(
                    0,
                    0,
                    Chunk.Size,
                    Chunk.Size));
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
        var localY =
            placement.AnchorY %
            Chunk.Size;
        var waterId =
            fluids.GetId(
                water.Id);

        Assert.True(
            chunk.GetCell(
                    8,
                    localY,
                    8)
                .IsEmpty);
        Assert.Equal(
            waterId,
            chunk.GetFluid(
                    8,
                    localY,
                    8)
                .Fluid);

        Assert.True(
            chunk.GetCell(
                    9,
                    localY,
                    8)
                .IsEmpty);
        Assert.True(
            chunk.GetFluid(
                    9,
                    localY,
                    8)
                .IsEmpty);

        Assert.True(
            chunk.GetCell(
                    8,
                    localY + 1,
                    8)
                .IsEmpty);
        Assert.True(
            chunk.GetFluid(
                    8,
                    localY + 1,
                    8)
                .IsEmpty);
    }

    [Fact]
    public void DimensionJsonParsesBiomeMarginStructurePlacement()
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
                  "surfaceBiomes": ["asteria:test/ocean"],
                  "generatedSurfaceStructures": [
                    {
                      "biome": "asteria:test/ocean",
                      "structure": "asteria:mouth",
                      "spacing": 64,
                      "chance": 1,
                      "jitter": 8,
                      "placement": "biomeMargin"
                    }
                  ]
                }
                """);

        Assert.Equal(
            DimensionGeneratedSurfaceStructurePlacement.BiomeMargin,
            Assert.Single(
                    definition.GeneratedSurfaceStructures)
                .Placement);
    }
}
