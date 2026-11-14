using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PartialDensityBoundaryCacheTests
{
    [Fact]
    public void PartialReadsMergeWithLaterFullColumnReads()
    {
        var generator = CreateGenerator();
        var point = FindVolumeSolid(generator, y: 96);
        var position = VoxelCoordinates.FromWorld(point.X, 96, point.Z);
        var columnIndices = new[]
        {
            position.Local.Z * Chunk.Size + position.Local.X,
        };
        var requestedMask = (ushort)(
            (1u << 0) | (1u << 6) | (1u << 13));
        var requestedMasks = new[] { requestedMask };
        var cache = new SurfaceTerrainColumnCache(generator.Terrain);
        var partialMask = new ushort[1];

        cache.ReadDensityColumnMasks(
            position.Chunk.X,
            96,
            position.Chunk.Z,
            columnIndices,
            requestedMasks,
            partialMask,
            out var partialProbes,
            out _, out _, out _, out _, out _);

        Assert.Equal(
            System.Numerics.BitOperations.PopCount((uint)requestedMask),
            partialProbes);
        Assert.Equal(
            (ushort)0,
            (ushort)(partialMask[0] & (ushort)~requestedMask));

        var expectedPartialSolidMask = (ushort)0;
        for (var localY = 0; localY < Chunk.Size; localY++)
        {
            if ((requestedMask & (1u << localY)) == 0)
                continue;

            var expected =
                generator.DensityAt(point.X, 96 + localY, point.Z) >= 0d;
            var actual = (partialMask[0] & (1u << localY)) != 0;
            Assert.Equal(expected, actual);
            if (expected)
                expectedPartialSolidMask |= (ushort)(1u << localY);
        }

        var fullMask = new ushort[1];
        cache.ReadDensityColumnMasks(
            position.Chunk.X,
            96,
            position.Chunk.Z,
            columnIndices,
            [ushort.MaxValue],
            fullMask,
            out var remainingProbes,
            out _, out _, out _, out _, out _);

        Assert.Equal(
            Chunk.Size -
            System.Numerics.BitOperations.PopCount((uint)requestedMask),
            remainingProbes);

        for (var localY = 0; localY < Chunk.Size; localY++)
        {
            var expected =
                generator.DensityAt(point.X, 96 + localY, point.Z) >= 0d;
            var actual = (fullMask[0] & (1u << localY)) != 0;
            Assert.Equal(expected, actual);
        }

        var cachedPartialMask = new ushort[1];
        cache.ReadDensityColumnMasks(
            position.Chunk.X,
            96,
            position.Chunk.Z,
            columnIndices,
            requestedMasks,
            cachedPartialMask,
            out var cachedProbes,
            out _, out _, out _, out _, out _);

        Assert.Equal(0, cachedProbes);
        Assert.Equal(expectedPartialSolidMask, cachedPartialMask[0]);
    }

    private static BiomeWorldGenerator CreateGenerator()
    {
        var surfaceBiome = new BiomeDefinition(
            "asteria:test/flat",
            new BiomeSurfaceLayoutDefinition(),
            new BiomeTerrainDefinition(32f, 0f, 64, 0f, 32),
            [
                new BiomeSurfaceLayerDefinition("asteria:block_grass", 1),
                new BiomeSurfaceLayerDefinition("asteria:dirt", 4),
                new BiomeSurfaceLayerDefinition("asteria:stone"),
            ]);

        var additive = new BiomeAdditiveDensityDefinition(
            minY: 80,
            maxY: 112,
            horizontalScale: 64,
            detailScale: 24,
            coverage: 1f,
            roughness: 0.2f,
            densityScale: 30f);
        var volumeBiome = new BiomeDefinition(
            "asteria:test/floating",
            surfaceLayout: null,
            surfaceTerrain: null,
            [
                new BiomeSurfaceLayerDefinition("asteria:block_grass", 1),
                new BiomeSurfaceLayerDefinition("asteria:dirt", 4),
                new BiomeSurfaceLayerDefinition("asteria:stone"),
            ],
            terrain3d: new BiomeTerrain3dDefinition([additive]),
            volumeLayout: new BiomeVolumeLayoutDefinition());

        var caves = new DimensionCaveDefinition(
        [
            new DimensionCaveLayerDefinition(
                minDepth: 10,
                maxDepth: 80,
                channels:
                [
                    new DimensionCaveNoiseChannelDefinition(56, 36),
                    new DimensionCaveNoiseChannelDefinition(56, 36),
                ],
                noiseHalfWidth: 1f,
                densityScale: 24f,
                boundaryFade: 6),
        ]);

        var dimension = new DimensionDefinition(
            new DimensionId("asteria:test"),
            [surfaceBiome.Id],
            64,
            18f,
            new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255),
                1f,
                new DimensionColor(0, 0, 0),
                0f),
            null,
            caves,
            volumeBiomes: [volumeBiome.Id]);

        var blocks = new BlockRegistry(
        [
            new BlockDefinition("asteria:stone"),
            new BlockDefinition("asteria:block_grass"),
            new BlockDefinition("asteria:dirt"),
        ]);

        return new BiomeWorldGenerator(
            8192UL,
            dimension,
            blocks,
            new BiomeRegistry([surfaceBiome, volumeBiome]));
    }

    private static (int X, int Z) FindVolumeSolid(
        BiomeWorldGenerator generator,
        int y)
    {
        for (var z = -2048; z <= 2048; z += 16)
        for (var x = -2048; x <= 2048; x += 16)
        {
            if (generator.VolumeBiomes.Sample(x, y, z) is not null &&
                generator.DensityAt(x, y, z) >= 0d)
                return (x, z);
        }

        throw new Xunit.Sdk.XunitException(
            $"Could not find a solid volume biome sample at Y={y}.");
    }
}
