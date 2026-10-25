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
}
