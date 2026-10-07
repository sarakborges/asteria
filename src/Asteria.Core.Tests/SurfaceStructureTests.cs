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
            4,
            structures.Count);
        Assert.Equal(
            new[]
            {
                "asteria:boulder_big",
                "asteria:boulder_huge",
                "asteria:boulder_medium",
                "asteria:boulder_small",
            },
            structures
                .Definitions()
                .Select(
                    definition =>
                        definition.Id)
                .ToArray());
        Assert.Equal(
            23,
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
