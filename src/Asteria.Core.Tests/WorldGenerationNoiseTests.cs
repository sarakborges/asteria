using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldGenerationNoiseTests
{
    [Theory]
    [InlineData(-31.25d, 7.75d, -19.5d, 8u, 6u)]
    [InlineData(13.125d, 24.875d, -42.5d, 16u, 12u)]
    [InlineData(-128.5d, 3.25d, 64.75d, 4u, 3u)]
    public void PreparedContinuousNoisePreservesScalarCornerHashExactly(
        double x,
        double y,
        double z,
        uint horizontalScale,
        uint verticalScale)
    {
        const ulong seed = 0xA57E_3D10_2026UL;
        var domain = GenerationDomain.Named("tests/prepared-continuous-noise");
        var prepared = WorldGenerationEntropy.PrepareValueNoise3D(
            x, y, z, horizontalScale, verticalScale);

        var expected = ReferenceContinuousNoise(
            seed, domain, x, y, z, horizontalScale, verticalScale);
        var actual = prepared.Sample(seed, domain);

        Assert.Equal(expected, actual);
    }

    [Fact]
    public void LatticeCachePreservesNoiseAcrossHitsAndDirectMappedCollisions()
    {
        const ulong seed = 0xA57E_3D10_2026UL;
        var domain = GenerationDomain.Named("tests/colliding-lattice-cache");
        var prepared = WorldGenerationEntropy.PrepareValueNoise3D(
            -31.25d, 7.75d, -19.5d, 8u, 6u);
        var expected = prepared.Sample(seed, domain);

        using var cache = new WorldGenerationEntropy.ValueNoise3DLatticeCache(
            capacity: 1);

        var firstKey = 0x1234_5678_9ABC_DEF0UL;
        var secondKey = 0x0FED_CBA9_8765_4321UL;
        var firstValue = cache.Sample(firstKey);
        Assert.Equal(firstValue, cache.Sample(firstKey));

        // A one-slot cache forces every distinct lattice corner to evict the
        // previous one. Eviction may affect work, never the sampled result.
        Assert.Equal(expected, prepared.Sample(seed, domain, cache));
        _ = cache.Sample(secondKey);
        Assert.Equal(firstValue, cache.Sample(firstKey));
        Assert.Equal(expected, prepared.Sample(seed, domain, cache));
    }

    private static double ReferenceContinuousNoise(
        ulong seed,
        GenerationDomain domain,
        double x,
        double y,
        double z,
        uint horizontalScale,
        uint verticalScale)
    {
        var gx = x / horizontalScale;
        var gy = y / verticalScale;
        var gz = z / horizontalScale;
        var floorX = Math.Floor(gx);
        var floorY = Math.Floor(gy);
        var floorZ = Math.Floor(gz);
        var x0 = (int)floorX;
        var y0 = (int)floorY;
        var z0 = (int)floorZ;
        var x1 = x0 + 1;
        var y1 = y0 + 1;
        var z1 = z0 + 1;
        var tx = WorldGenerationEntropy.SmoothStep(gx - floorX);
        var ty = WorldGenerationEntropy.SmoothStep(gy - floorY);
        var tz = WorldGenerationEntropy.SmoothStep(gz - floorZ);

        double Lattice(int lx, int ly, int lz) =>
            WorldGenerationEntropy.SignedUnit(
                WorldGenerationEntropy.Sample3D(seed, domain, lx, ly, lz));
        double Lerp(double left, double right, double amount) =>
            left + (right - left) * amount;

        var firstZ = Lerp(
            Lerp(Lattice(x0, y0, z0), Lattice(x1, y0, z0), tx),
            Lerp(Lattice(x0, y1, z0), Lattice(x1, y1, z0), tx),
            ty);
        var secondZ = Lerp(
            Lerp(Lattice(x0, y0, z1), Lattice(x1, y0, z1), tx),
            Lerp(Lattice(x0, y1, z1), Lattice(x1, y1, z1), tx),
            ty);
        return Lerp(firstZ, secondZ, tz);
    }

    [Theory]
    [InlineData(-31, 7, -19, 8u, 6u)]
    [InlineData(13, 24, -42, 16u, 12u)]
    [InlineData(-128, 3, 64, 4u, 3u)]
    public void ChunkLocalNoiseSamplerMatchesScalarNoiseExactly(
        int originX,
        int originY,
        int originZ,
        uint horizontalScale,
        uint verticalScale)
    {
        const ulong seed = 0xA57E_3D10_2026UL;
        var domain = GenerationDomain.Named("tests/chunk-noise-sampler");
        const int size = Chunk.Size;
        var sampler = new WorldGenerationEntropy.ValueNoise3DGridSampler(
            seed,
            domain,
            originX,
            originY,
            originZ,
            size,
            size,
            size,
            horizontalScale,
            verticalScale);

        for (var localZ = 0; localZ < size; localZ++)
        for (var localY = 0; localY < size; localY++)
        for (var localX = 0; localX < size; localX++)
        {
            var expected = WorldGenerationEntropy.ValueNoise3D(
                seed,
                domain,
                originX + localX,
                originY + localY,
                originZ + localZ,
                horizontalScale,
                verticalScale);
            var actual = sampler.Sample(localX, localY, localZ);

            Assert.Equal(expected, actual);
        }
    }

    [Theory]
    [InlineData(-31, 7, -19, 1, 16, 16, 8u, 6u)]
    [InlineData(13, 24, -42, 16, 16, 1, 16u, 12u)]
    [InlineData(-128, 3, 64, 1, 16, 1, 4u, 3u)]
    [InlineData(-31, 7, -19, 6, 16, 16, 56u, 36u)]
    [InlineData(13, 24, -42, 16, 16, 6, 56u, 36u)]
    public void ColumnMajorNoiseSamplerMatchesScalarNoiseExactly(
        int originX,
        int originY,
        int originZ,
        int width,
        int height,
        int depth,
        uint horizontalScale,
        uint verticalScale)
    {
        const ulong seed = 0xA57E_3D10_2026UL;
        var domain = GenerationDomain.Named("tests/column-major-noise-sampler");
        var sampler = new WorldGenerationEntropy.ValueNoise3DGridSampler(
            seed,
            domain,
            originX,
            originY,
            originZ,
            width,
            height,
            depth,
            horizontalScale,
            verticalScale);

        // Horizontal boundary batches keep X fixed while visiting the
        // requested Z strip, then scan the complete vertical column.
        // This exercises both cached X and X/Y interpolation across Z.
        for (var localX = 0; localX < width; localX++)
        for (var localZ = 0; localZ < depth; localZ++)
        for (var localY = 0; localY < height; localY++)
        {
            var expected = WorldGenerationEntropy.ValueNoise3D(
                seed,
                domain,
                originX + localX,
                originY + localY,
                originZ + localZ,
                horizontalScale,
                verticalScale);
            var actual = sampler.Sample(localX, localY, localZ);
            Assert.Equal(expected, actual);
        }
    }

}
