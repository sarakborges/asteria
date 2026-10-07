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
        var structures =
            LoadDefaultStructures();

        dimensions.ValidateBiomes(
            biomes);
        var blocks =
            LoadDefaultBlocks();
        dimensions.ValidateBlocks(
            blocks);
        dimensions.ValidateFluids(
            fluids);
        structures.ValidateBlocks(
            blocks);
        dimensions.ValidateStructures(
            structures);

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
            overworld.SurfaceBiomes.Count);
        Assert.Single(
            overworld.VolumeBiomes);
        Assert.Single(
            overworld.UndergroundBiomes);
        Assert.Contains(
            "asteria:overworld/caverns",
            overworld.UndergroundBiomes);
        Assert.Contains(
            "asteria:overworld/ocean",
            overworld.SurfaceBiomes);
        Assert.Contains(
            "asteria:overworld/enchanted_forest",
            overworld.SurfaceBiomes);
        Assert.Contains(
            "asteria:overworld/alps",
            overworld.SurfaceBiomes);
        Assert.DoesNotContain(
            "asteria:overworld/floating_islands",
            overworld.SurfaceBiomes);
        Assert.Contains(
            "asteria:overworld/floating_islands",
            overworld.VolumeBiomes);
        Assert.Contains(
            "asteria:overworld/volcano",
            overworld.SurfaceBiomes);
        Assert.Equal(
            "asteria:overworld/ocean",
            overworld.GeneratedOcean?.Biome);
        Assert.Equal(
            "asteria:water",
            overworld.GeneratedOcean?.Fluid);
        Assert.Equal(
            4,
            overworld.GeneratedOcean?.Shore.ShelfDepth);
        Assert.Equal(
            2,
            overworld.GeneratedOcean?.Shore.BeachHeight);
        Assert.Equal(
            0.62f,
            overworld.GeneratedOcean?.Shore.BeachStartDominance);
        Assert.Equal(
            0.72f,
            overworld.GeneratedOcean?.Shore.ShelfStartDominance);
        Assert.Equal(
            0.85f,
            overworld.GeneratedOcean?.Shore.DeepWaterStartDominance);
        Assert.Null(
            umbral.GeneratedOcean);
        Assert.Equal(
            3,
            umbral.SurfaceBiomes.Count);
        Assert.Empty(
            umbral.VolumeBiomes);
        Assert.Empty(
            umbral.UndergroundBiomes);
        Assert.All(
            overworld.SurfaceBiomes,
            biome =>
                Assert.StartsWith(
                    "asteria:overworld/",
                    biome));
        Assert.All(
            overworld.VolumeBiomes,
            biome =>
                Assert.StartsWith(
                    "asteria:overworld/",
                    biome));
        Assert.All(
            overworld.UndergroundBiomes,
            biome =>
                Assert.StartsWith(
                    "asteria:overworld/",
                    biome));
        Assert.All(
            umbral.SurfaceBiomes,
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
                dimension.SurfaceBiomes.ToHashSet(
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
        var fluids =
            LoadDefaultFluids();
        var structures =
            LoadDefaultStructures();
        var biomes =
            LoadDefaultBiomes();
        var dimensions =
            LoadDefaultDimensions();
        dimensions.ValidateBiomes(
            biomes);
        dimensions.ValidateFluids(
            fluids);

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
                fluids,
                biomes,
                structures);
        var umbralGenerator =
            new BiomeWorldGenerator(
                DimensionSeed.Derive(
                    worldSeed,
                    umbral.Id),
                umbral,
                blocks,
                fluids,
                biomes,
                structures);

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
        var structures =
            LoadDefaultStructures();
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
                biomes,
                structures);
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
    public void DefaultOverworldOceanEndsInDryOceanOwnedBeach()
    {
        const ulong worldSeed =
            0xA57E_2026UL;
        var blocks =
            LoadDefaultBlocks();
        var fluids =
            LoadDefaultFluids();
        var structures =
            LoadDefaultStructures();
        var biomes =
            LoadDefaultBiomes();
        var dimensions =
            LoadDefaultDimensions();
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
                biomes,
                structures);
        var boundary =
            FindOceanBoundaryWithDeepWater(
                generator,
                dimension.SeaLevel);
        var beachY =
            generator.SurfaceHeight(
                boundary.OceanX,
                boundary.Z);

        Assert.Equal(
            "asteria:overworld/ocean",
            generator.Biomes.Sample(
                boundary.OceanX,
                boundary.Z).Primary);
        Assert.True(
            beachY >=
            dimension.SeaLevel);

        var beach =
            VoxelCoordinates.FromWorld(
                boundary.OceanX,
                beachY,
                boundary.Z);
        var beachChunk =
            generator.Materialize(
                beach.Chunk);
        Assert.Equal(
            blocks.GetId(
                "asteria:sand"),
            beachChunk.GetBlock(
                beach.Local.X,
                beach.Local.Y,
                beach.Local.Z));

        var beachSeaLevel =
            VoxelCoordinates.FromWorld(
                boundary.OceanX,
                dimension.SeaLevel,
                boundary.Z);
        var beachSeaChunk =
            beachSeaLevel.Chunk ==
                beach.Chunk
                ? beachChunk
                : generator.Materialize(
                    beachSeaLevel.Chunk);
        Assert.True(
            beachSeaChunk.GetFluid(
                beachSeaLevel.Local.X,
                beachSeaLevel.Local.Y,
                beachSeaLevel.Local.Z).IsEmpty);

        var deepX =
            boundary.OceanX +
            boundary.InteriorDirection *
            boundary.DeepWaterDistance;
        var deep =
            VoxelCoordinates.FromWorld(
                deepX,
                dimension.SeaLevel,
                boundary.Z);
        var deepChunk =
            generator.Materialize(
                deep.Chunk);
        var water =
            deepChunk.GetFluid(
                deep.Local.X,
                deep.Local.Y,
                deep.Local.Z);

        Assert.Equal(
            fluids.GetId(
                "asteria:water"),
            water.Fluid);
        Assert.True(
            water.IsSource);
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

    private static OceanBoundary FindOceanBoundaryWithDeepWater(
        BiomeWorldGenerator generator,
        int seaLevel)
    {
        const string ocean =
            "asteria:overworld/ocean";
        const int minimum =
            -4096;
        const int maximum =
            4096;
        const int coarseStep =
            16;

        for (var z = minimum;
             z <= maximum;
             z += 64)
        {
            var previousX =
                minimum;
            var previousOcean =
                generator.Biomes.Sample(
                    previousX,
                    z).Primary ==
                ocean;

            for (var x =
                     minimum +
                     coarseStep;
                 x <= maximum;
                 x += coarseStep)
            {
                var currentOcean =
                    generator.Biomes.Sample(
                        x,
                        z).Primary ==
                    ocean;

                if (currentOcean ==
                    previousOcean)
                {
                    previousX = x;
                    previousOcean =
                        currentOcean;
                    continue;
                }

                var leftOcean =
                    generator.Biomes.Sample(
                        previousX,
                        z).Primary ==
                    ocean;

                for (var exactX =
                         previousX + 1;
                     exactX <= x;
                     exactX++)
                {
                    var rightOcean =
                        generator.Biomes.Sample(
                            exactX,
                            z).Primary ==
                        ocean;

                    if (rightOcean ==
                        leftOcean)
                    {
                        continue;
                    }

                    var oceanX =
                        leftOcean
                            ? exactX - 1
                            : exactX;
                    var direction =
                        leftOcean
                            ? -1
                            : 1;

                    for (var distance = 1;
                         distance <= 512;
                         distance++)
                    {
                        var candidateX =
                            oceanX +
                            direction *
                            distance;
                        var sample =
                            generator.Biomes.Sample(
                                candidateX,
                                z);

                        if (sample.Primary !=
                            ocean)
                        {
                            break;
                        }

                        if (generator.SurfaceHeight(
                                candidateX,
                                z) <
                            seaLevel)
                        {
                            return new OceanBoundary(
                                oceanX,
                                z,
                                direction,
                                distance);
                        }
                    }

                    leftOcean =
                        rightOcean;
                }

                previousX = x;
                previousOcean =
                    currentOcean;
            }
        }

        throw new Xunit.Sdk.XunitException(
            "Could not find an ocean boundary with deep water behind its shore.");
    }

    private readonly record struct OceanBoundary(
        int OceanX,
        int Z,
        int InteriorDirection,
        int DeepWaterDistance);

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

    private static StructureRegistry LoadDefaultStructures() =>
        StructureRegistry.FromJson(
            ReadJsonDirectory(
                "structures"));

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
