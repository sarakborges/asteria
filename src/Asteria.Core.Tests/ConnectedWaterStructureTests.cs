using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ConnectedWaterStructureTests
{
    [Fact]
    public void DefaultConnectedWaterStructuresParseAndValidate()
    {
        var blocks =
            BlockRegistry.FromJson(
                ReadJsonDirectory(
                    "blocks"));
        var fluids =
            FluidRegistry.FromJson(
                ReadJsonDirectory(
                    "fluids"));
        var structures =
            StructureRegistry.FromJson(
                ReadJsonDirectory(
                    "structures"));
        var dimensions =
            DimensionRegistry.FromJson(
                ReadJsonDirectory(
                    "dimensions"));

        structures.ValidateBlocks(
            blocks);
        structures.ValidateFluids(
            fluids);
        dimensions.ValidateStructures(
            structures);

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

        var waterfall =
            structures.Get(
                "asteria:mountain_waterfall_01");
        Assert.Equal(
            3,
            waterfall.Restrictions.MinSlope);
        Assert.Equal(
            -1,
            waterfall.GroundAnchorY);
        Assert.Equal(
            2,
            waterfall.ClearAbove);
        Assert.NotEmpty(
            waterfall.FluidVoxels);

        var mouth =
            structures.Get(
                "asteria:river_ocean_mouth");
        Assert.NotEmpty(
            mouth.FluidVoxels);
        Assert.NotEmpty(
            mouth.ClearVoxels);
        Assert.Contains(
            mouth.Connectors,
            connector =>
                string.Equals(
                    connector.Target,
                    "asteria:river_segment",
                    StringComparison.Ordinal));

        var overworld =
            dimensions.Get(
                DimensionId.Overworld);
        var mouthRoot =
            Assert.Single(
                overworld
                    .GeneratedSurfaceStructures
                    .Where(root =>
                        string.Equals(
                            root.Structure,
                            "asteria:river_ocean_mouth",
                            StringComparison.Ordinal)));
        Assert.Equal(
            DimensionGeneratedSurfaceStructurePlacement.BiomeMargin,
            mouthRoot.Placement);
    }

    [Fact]
    public void StructureFluidAndClearPayloadsMaterializeThroughSingleWriter()
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
                "asteria:test_water_payload",
                rotation: false,
                anchor: default,
                voxels:
                    Array.Empty<StructureVoxelDefinition>(),
                restrictions:
                    new StructureRestrictionsDefinition(
                        maxSlope: 0,
                        requiresDryGround: false,
                        requiredBiomeCoverage: 1f),
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
                        "asteria:water"),
                ],
                clearVoxels:
                [
                    new StructureClearVoxelDefinition(
                        1,
                        0,
                        0),
                ],
                groundAnchorY: 0);
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
                structures);

        var chunk =
            generator.Materialize(
                new ChunkCoord(
                    0,
                    2,
                    0));
        var water =
            fluids.GetId(
                "asteria:water");

        Assert.True(
            chunk.GetCell(
                    8,
                    0,
                    8)
                .IsEmpty);
        Assert.Equal(
            water,
            chunk.GetFluid(
                    8,
                    0,
                    8)
                .Fluid);
        Assert.True(
            chunk.GetCell(
                    9,
                    0,
                    8)
                .IsEmpty);
        Assert.True(
            chunk.GetFluid(
                    9,
                    0,
                    8)
                .IsEmpty);
    }

    [Fact]
    public void StructureJsonParsesFluidClearAndGroundFitFields()
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
                    "maxSlope": 5,
                    "requiresDryGround": false,
                    "requiredBiomeCoverage": 0.5
                  },
                  "anchor": {
                    "x": 1,
                    "y": 0,
                    "z": 0
                  },
                  "groundAnchorY": -1,
                  "clearAbove": 3,
                  "palette": {
                    "W": {
                      "fluid": "asteria:water"
                    },
                    "C": {
                      "clear": true
                    }
                  },
                  "layers": [
                    {
                      "y": -1,
                      "rows": [
                        "WC"
                      ]
                    }
                  ]
                }
                """);

        Assert.False(
            definition.Locatable);
        Assert.Equal(
            2,
            definition.Restrictions.MinSlope);
        Assert.Equal(
            5,
            definition.Restrictions.MaxSlope);
        Assert.Equal(
            -1,
            definition.GroundAnchorY);
        Assert.Equal(
            3,
            definition.ClearAbove);
        Assert.Single(
            definition.FluidVoxels);
        Assert.Single(
            definition.ClearVoxels);
        Assert.Empty(
            definition.Voxels);
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
