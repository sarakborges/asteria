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

        dimensions.ValidateBiomes(
            biomes);

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
            10,
            overworld.Biomes.Count);
        Assert.DoesNotContain(
            "asteria:overworld/ocean",
            overworld.Biomes);
        Assert.DoesNotContain(
            "asteria:overworld/enchanted_forest",
            overworld.Biomes);
        Assert.Contains(
            "asteria:overworld/alps",
            overworld.Biomes);
        Assert.Contains(
            "asteria:overworld/volcano",
            overworld.Biomes);
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
        Assert.True(
            umbral.Environment.FogDensity >
            overworld.Environment.FogDensity);
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
    public void OverworldAndUmbralMaterializeIndependentWorlds()
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

        var overworldChunk =
            overworldGenerator.Materialize(
                ChunkCoord.Zero);
        var umbralChunk =
            umbralGenerator.Materialize(
                ChunkCoord.Zero);

        Assert.NotEqual(
            ContentFingerprint(
                overworldChunk),
            ContentFingerprint(
                umbralChunk));
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
                baseHeight: 8f,
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
