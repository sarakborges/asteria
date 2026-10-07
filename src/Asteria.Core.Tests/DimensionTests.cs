using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DimensionTests
{
    [Fact]
    public void DefaultPackDefinesOverworldAndUmbral()
    {
        var biomes =
            LoadDefaultBiomes();
        var dimensions =
            LoadDefaultDimensions();
        var fluids =
            LoadDefaultFluids();

        dimensions.ValidateBiomes(
            biomes);
        var blocks =
            LoadDefaultBlocks();
        dimensions.ValidateBlocks(
            blocks);
        dimensions.ValidateFluids(
            fluids);

        Assert.Equal(
            2,
            dimensions.Count);

        var overworld =
            dimensions.Get(
                DimensionId.Overworld);
        var umbral =
            dimensions.Get(
                new DimensionId(
                    "asteria:umbral"));

        Assert.Equal(
            12,
            overworld.Biomes.Count);
        Assert.Contains(
            "asteria:overworld/ocean",
            overworld.Biomes);
        Assert.DoesNotContain(
            "asteria:overworld/enchanted_forest",
            overworld.Biomes);
        Assert.Contains(
            "asteria:overworld/alps",
            overworld.Biomes);
        Assert.Contains(
            "asteria:overworld/floating_islands",
            overworld.Biomes);
        Assert.Contains(
            "asteria:overworld/volcano",
            overworld.Biomes);
        Assert.Equal(
            "asteria:overworld/ocean",
            overworld.GeneratedOcean?.Biome);
        Assert.Equal(
            "asteria:water",
            overworld.GeneratedOcean?.Fluid);
        Assert.Null(
            umbral.GeneratedOcean);
        Assert.Equal(
            3,
            umbral.Biomes.Count);
        Assert.All(
            overworld.Biomes,
            biome =>
                Assert.StartsWith(
                    "asteria:overworld/",
                    biome));
        Assert.All(
            umbral.Biomes,
            biome =>
                Assert.StartsWith(
                    "asteria:umbral/",
                    biome));

        Assert.Equal(
            90,
            overworld.SeaLevel);
        Assert.Equal(
            90,
            umbral.SeaLevel);
        Assert.Equal(
            18f,
            overworld.GravityStrength);
        Assert.Equal(
            18f,
            umbral.GravityStrength);
        Assert.Equal(
            "asteria:sphere_shell",
            overworld.Shell?.Block);
        Assert.Equal(
            0,
            overworld.Shell?.FloorY);
        Assert.Null(
            overworld.Shell?.RoofY);
        Assert.Equal(
            "asteria:sphere_shell",
            umbral.Shell?.Block);
        Assert.Equal(
            0,
            umbral.Shell?.FloorY);
        Assert.Null(
            umbral.Shell?.RoofY);
        Assert.True(
            umbral.Environment.FogDensity >
            overworld.Environment.FogDensity);
    }

    [Fact]
    public void SphereShellRejectsNegativeFloor()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () =>
                new DimensionShellDefinition(
                    "asteria:sphere_shell",
                    floorY: -1));
    }

    [Fact]
    public void DimensionSeedsAreStableAndDistinct()
    {
        const ulong worldSeed =
            0xA57E_2026UL;
        var overworld =
            DimensionSeed.Derive(
                worldSeed,
                DimensionId.Overworld);
        var umbral =
            DimensionSeed.Derive(
                worldSeed,
                new DimensionId(
                    "asteria:umbral"));

        Assert.Equal(
            overworld,
            DimensionSeed.Derive(
                worldSeed,
                DimensionId.Overworld));
        Assert.NotEqual(
            overworld,
            umbral);
    }

    [Fact]
    public void DimensionBiomePoolIsExplicit()
    {
        var biomes =
            LoadDefaultBiomes();
        var dimensions =
            LoadDefaultDimensions();
        var worldSeed =
            91UL;

        foreach (var dimension in
                 dimensions.Definitions())
        {
            var field =
                new BiomeField(
                    DimensionSeed.Derive(
                        worldSeed,
                        dimension.Id),
                    dimension,
                    biomes);
            var allowed =
                dimension.Biomes.ToHashSet(
                    StringComparer.Ordinal);
            var grid =
                field.SampleGrid(
                    -512,
                    -512,
                    width: 32,
                    depth: 32,
                    step: 32);

            for (var z = 0;
                 z < grid.Depth;
                 z++)
            {
                for (var x = 0;
                     x < grid.Width;
                     x++)
                {
                    Assert.Contains(
                        grid[x, z].Primary,
                        allowed);
                    Assert.All(
                        grid[x, z].Influences,
                        influence =>
                            Assert.Contains(
                                influence.BiomeId,
                                allowed));
                }
            }
        }
    }

    [Fact]
    public void OverworldAndUmbralMaterializationRemainIndependentWhenInterleaved()
    {
        const ulong worldSeed =
            0xA57E_2026UL;
        var blocks =
            LoadDefaultBlocks();
        var biomes =
            LoadDefaultBiomes();
        var dimensions =
            LoadDefaultDimensions();
        dimensions.ValidateBiomes(
            biomes);

        var overworld =
            dimensions.Get(
                DimensionId.Overworld);
        var umbral =
            dimensions.Get(
                new DimensionId(
                    "asteria:umbral"));
        var overworldGenerator =
            new BiomeWorldGenerator(
                DimensionSeed.Derive(
                    worldSeed,
                    overworld.Id),
                overworld,
                blocks,
                biomes);
        var umbralGenerator =
            new BiomeWorldGenerator(
                DimensionSeed.Derive(
                    worldSeed,
                    umbral.Id),
                umbral,
                blocks,
                biomes);

        Assert.Equal(
            DimensionId.Overworld,
            overworldGenerator.DimensionId);
        Assert.Equal(
            new DimensionId(
                "asteria:umbral"),
            umbralGenerator.DimensionId);

        var overworldBefore =
            ContentFingerprint(
                overworldGenerator.Materialize(
                    ChunkCoord.Zero));
        var umbralBefore =
            ContentFingerprint(
                umbralGenerator.Materialize(
                    ChunkCoord.Zero));

        _ =
            overworldGenerator.Materialize(
                new ChunkCoord(
                    2,
                    6,
                    -3));
        _ =
            umbralGenerator.Materialize(
                new ChunkCoord(
                    -4,
                    5,
                    1));

        Assert.Equal(
            overworldBefore,
            ContentFingerprint(
                overworldGenerator.Materialize(
                    ChunkCoord.Zero)));
        Assert.Equal(
            umbralBefore,
            ContentFingerprint(
                umbralGenerator.Materialize(
                    ChunkCoord.Zero)));
    }

    [Fact]
    public void DefaultOverworldGeneratedOceanFillsToSeaLevel()
    {
        const ulong worldSeed =
            0xA57E_2026UL;
        var blocks =
            LoadDefaultBlocks();
        var fluids =
            LoadDefaultFluids();
        var biomes =
            LoadDefaultBiomes();
        var dimensions =
            LoadDefaultDimensions();

        dimensions.ValidateBlocks(blocks);
        dimensions.ValidateFluids(fluids);
        dimensions.ValidateBiomes(biomes);

        var dimension =
            dimensions.Get(
                DimensionId.Overworld);
        var generator =
            new BiomeWorldGenerator(
                DimensionSeed.Derive(
                    worldSeed,
                    dimension.Id),
                dimension,
                blocks,
                fluids,
                biomes);
        var point =
            FindOceanColumn(
                generator,
                dimension.SeaLevel);
        var address =
            VoxelCoordinates.FromWorld(
                point.X,
                dimension.SeaLevel,
                point.Z);
        var chunk =
            generator.Materialize(
                address.Chunk);
        var fluid =
            chunk.GetFluid(
                address.Local.X,
                address.Local.Y,
                address.Local.Z);

        Assert.Equal(
            fluids.GetId("asteria:water"),
            fluid.Fluid);
        Assert.True(fluid.IsSource);
        Assert.Equal(
            FluidCell.MaxLevel,
            fluid.Level);
        Assert.True(
            chunk.GetCell(
                address.Local.X,
                address.Local.Y,
                address.Local.Z).IsEmpty);

        var above =
            VoxelCoordinates.FromWorld(
                point.X,
                dimension.SeaLevel + 1,
                point.Z);
        var aboveChunk =
            above.Chunk == address.Chunk
                ? chunk
                : generator.Materialize(
                    above.Chunk);

        Assert.True(
            aboveChunk.GetFluid(
                above.Local.X,
                above.Local.Y,
                above.Local.Z).IsEmpty);

        var horizontal =
            VoxelCoordinates.FromWorld(
                point.X,
                0,
                point.Z).Chunk;
        Assert.True(
            generator.GetSurfaceRange(
                horizontal.X,
                horizontal.Z).MaximumWorldY >=
            dimension.SeaLevel);
    }

    [Fact]
    public void DimensionRejectsMissingGeneratedOceanFluid()
    {
        var dimensions =
            new DimensionRegistry(
            [
                new DimensionDefinition(
                    new DimensionId(
                        "asteria:test"),
                    [
                        "asteria:test/ocean",
                    ],
                    seaLevel: 32,
                    gravityStrength: 18f,
                    new DimensionSpawnDefinition(
                        0,
                        0),
                    TestEnvironment(),
                    generatedOcean:
                        new DimensionGeneratedOceanDefinition(
                            "asteria:test/ocean",
                            "asteria:missing")),
            ]);
        var fluids =
            new FluidRegistry(
            [
                new FluidDefinition(
                    "asteria:water",
                    new FluidColor(
                        79,
                        159,
                        214),
                    0.72f),
            ]);

        Assert.Throws<ArgumentException>(
            () =>
                dimensions.ValidateFluids(
                    fluids));
    }

    [Fact]
    public void DimensionRejectsBiomeOwnedByAnotherDimension()
    {
        var biomes =
            new BiomeRegistry(
            [
                TestBiome(
                    "asteria:overworld/plain"),
            ]);
        var dimensions =
            new DimensionRegistry(
            [
                new DimensionDefinition(
                    new DimensionId(
                        "asteria:umbral"),
                    [
                        "asteria:overworld/plain",
                    ],
                    seaLevel: 90,
                    18f,
                    new DimensionSpawnDefinition(
                        0,
                        0),
                    TestEnvironment()),
            ]);

        Assert.Throws<ArgumentException>(
            () =>
                dimensions.ValidateBiomes(
                    biomes));
    }

    private static (int X, int Z) FindOceanColumn(
        BiomeWorldGenerator generator,
        int seaLevel)
    {
        for (var z = -4096;
             z <= 4096;
             z += 64)
        {
            for (var x = -4096;
                 x <= 4096;
                 x += 64)
            {
                if (generator.Biomes.Sample(
                        x,
                        z).Primary ==
                        "asteria:overworld/ocean" &&
                    generator.SurfaceHeight(
                        x,
                        z) <
                    seaLevel)
                {
                    return (x, z);
                }
            }
        }

        throw new Xunit.Sdk.XunitException(
            "Could not find a generated ocean column.");
    }

    private static ulong ContentFingerprint(
        Chunk chunk)
    {
        var hash =
            1469598103934665603UL;

        chunk.VisitBlockCells(
            (x, y, z, cell) =>
            {
                unchecked
                {
                    hash ^=
                        (ulong)(
                            x |
                            y << 8 |
                            z << 16);
                    hash *=
                        1099511628211UL;
                    hash ^=
                        cell.Block.Value;
                    hash *=
                        1099511628211UL;
                }
            });

        return hash;
    }

    private static DimensionEnvironmentDefinition
        TestEnvironment() =>
        new(
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
            0f);

    private static BiomeDefinition TestBiome(
        string id) =>
        new(
            id,
            new BiomeSurfaceLayoutDefinition(
                regionMin: 192,
                regionMax: 192),
            new BiomeTerrainDefinition(
                baseHeightOffset: 8f,
                macroAmplitude: 0f,
                macroScale: 128,
                detailAmplitude: 0f,
                detailScale: 32),
            [
                new BiomeSurfaceLayerDefinition(
                    "asteria:stone"),
            ]);

    private static BlockRegistry LoadDefaultBlocks() =>
        BlockRegistry.FromJson(
            ReadJsonDirectory(
                "blocks"));

    private static FluidRegistry LoadDefaultFluids() =>
        FluidRegistry.FromJson(
            ReadJsonDirectory(
                "fluids"));

    private static BiomeRegistry LoadDefaultBiomes() =>
        BiomeRegistry.FromJson(
            ReadJsonDirectory(
                "biomes"));

    private static DimensionRegistry LoadDefaultDimensions() =>
        DimensionRegistry.FromJson(
            ReadJsonDirectory(
                "dimensions"));

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
