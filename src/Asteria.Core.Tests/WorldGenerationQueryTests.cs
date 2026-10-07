using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldGenerationQueryTests
{
    [Fact]
    public void SurfaceBiomeSearchFindsTheOtherActiveBiomeWithinBound()
    {
        var first =
            TestBiome(
                "asteria:test/a");
        var second =
            TestBiome(
                "asteria:test/b");
        var registry =
            new BiomeRegistry(
            [
                first,
                second,
            ]);
        var dimension =
            TestDimension(
                [
                    first.Id,
                    second.Id,
                ]);
        var field =
            new BiomeField(
                123UL,
                dimension,
                registry);
        var origin =
            field.Sample(
                0,
                0);
        var target =
            string.Equals(
                origin.Primary,
                first.Id,
                StringComparison.Ordinal)
                ? second.Id
                : first.Id;

        var found =
            field.FindNearestSurfaceBiome(
                target,
                0,
                0,
                4096);

        Assert.NotNull(
            found);
        Assert.Equal(
            target,
            found!.Sample.Primary);
        var distanceSquared =
            (Int128)found.X *
            found.X +
            (Int128)found.Z *
            found.Z;
        Assert.True(
            distanceSquared <=
            (Int128)4096 *
            4096);

        var exact =
            field.FindNearestSurfaceBiome(
                origin.Primary,
                0,
                0,
                0);
        Assert.NotNull(
            exact);
        Assert.Equal(
            (0, 0),
            (
                exact!.X,
                exact.Z));

        Assert.Null(
            field.FindNearestSurfaceBiome(
                "asteria:test/missing",
                0,
                0,
                4096));
    }

    [Fact]
    public void GeneratedDestinationKeepsPreferredSafeColumn()
    {
        var generator =
            FlatGenerator();

        var destination =
            generator.FindGeneratedSurfaceDestination(
                0,
                0,
                4);

        Assert.Equal(
            new GeneratedSurfaceDestination(
                0,
                33,
                0),
            destination);
    }

    [Fact]
    public void GeneratedDestinationAvoidsShallowGeneratedFluid()
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
            TestBiome(
                "asteria:test/swamp");
        var dimension =
            TestDimension(
                [
                    biome.Id,
                ],
                generatedSurfaceFluids:
                [
                    new DimensionGeneratedSurfaceFluidDefinition(
                        biome.Id,
                        "asteria:water",
                        spacing: 16,
                        radius: 8,
                        jitter: 0,
                        chance: 1f,
                        depth: 1),
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
                ]));

        var destination =
            generator.FindGeneratedSurfaceDestination(
                8,
                8,
                10);

        Assert.NotNull(
            destination);
        Assert.NotEqual(
            (8, 8),
            (
                destination!.Value.X,
                destination.Value.Z));
        Assert.True(
            Math.Max(
                Math.Abs(
                    destination.Value.X -
                    8),
                Math.Abs(
                    destination.Value.Z -
                    8)) <=
            10);
    }

    [Fact]
    public void GeneratedDestinationAvoidsStructureFootprint()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
                new BlockDefinition(
                    "asteria:marker"),
            ]);
        var biome =
            TestBiome(
                "asteria:test/flat");
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
        var dimension =
            TestDimension(
                [
                    biome.Id,
                ],
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
                new FluidRegistry(
                    Array.Empty<FluidDefinition>()),
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
            (8, 8),
            (
                placement.AnchorX,
                placement.AnchorZ));

        var destination =
            generator.FindGeneratedSurfaceDestination(
                8,
                8,
                2);

        Assert.NotNull(
            destination);
        Assert.NotEqual(
            (8, 8),
            (
                destination!.Value.X,
                destination.Value.Z));
    }

    private static BiomeWorldGenerator
        FlatGenerator()
    {
        var blocks =
            new BlockRegistry(
            [
                new BlockDefinition(
                    "asteria:stone"),
            ]);
        var biome =
            TestBiome(
                "asteria:test/flat");

        return new BiomeWorldGenerator(
            77UL,
            TestDimension(
                [
                    biome.Id,
                ]),
            blocks,
            new FluidRegistry(
                Array.Empty<FluidDefinition>()),
            new BiomeRegistry(
            [
                biome,
            ]));
    }

    private static BiomeDefinition TestBiome(
        string id) =>
        new(
            id,
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

    private static DimensionDefinition TestDimension(
        IEnumerable<string> surfaceBiomes,
        IEnumerable<DimensionGeneratedSurfaceStructureDefinition>? generatedSurfaceStructures = null,
        IEnumerable<DimensionGeneratedSurfaceFluidDefinition>? generatedSurfaceFluids = null) =>
        new(
            new DimensionId(
                "asteria:test"),
            surfaceBiomes,
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
                generatedSurfaceStructures,
            generatedSurfaceFluids:
                generatedSurfaceFluids);
}
