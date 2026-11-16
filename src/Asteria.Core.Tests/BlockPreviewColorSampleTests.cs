using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockPreviewColorSampleTests
{
    [Fact]
    public void NormalizedBiomeSamplesBecomeExactSrgbPreviewBytes()
    {
        Assert.Equal(new BlockPreviewColor(0, 128, 255),
            BlockPreviewColor.FromNormalized(new Vector3(0f, .5f, 1f)));
        Assert.Equal(new BlockPreviewColor(17, 51, 221),
            BlockPreviewColor.FromNormalized(new Vector3(
                17f / 255f, 51f / 255f, 221f / 255f)));
    }

    [Fact]
    public void OutOfRangeValuesAreClampedWithoutWrapping()
    {
        Assert.Equal(new BlockPreviewColor(0, 255, 128),
            BlockPreviewColor.FromNormalized(new Vector3(-10f, 15f, .5f)));
    }

    [Theory]
    [InlineData(float.NaN, 0f, 0f)]
    [InlineData(0f, float.PositiveInfinity, 0f)]
    [InlineData(0f, 0f, float.NegativeInfinity)]
    public void RejectsNonFiniteChannels(float x, float y, float z)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            BlockPreviewColor.FromNormalized(new Vector3(x, y, z)));
    }
}
