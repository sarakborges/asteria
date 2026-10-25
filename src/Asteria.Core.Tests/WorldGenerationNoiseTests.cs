using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldGenerationNoiseTests
{
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
