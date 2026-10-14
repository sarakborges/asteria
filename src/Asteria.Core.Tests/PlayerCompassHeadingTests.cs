using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerCompassHeadingTests
{
    [Theory]
    [InlineData(0f, 0f, 180f)]
    [InlineData(-1.57079637f, 90f, 270f)]
    [InlineData(1.57079637f, 270f, 90f)]
    [InlineData(3.14159274f, 180f, 0f)]
    public void FrontCameraFlipsBearing(float yaw, float normal, float front)
    {
        Assert.Equal(normal, PlayerCompassHeading.QuantizedDegrees(yaw, false));
        Assert.Equal(front, PlayerCompassHeading.QuantizedDegrees(yaw, true));
    }

    [Fact]
    public void HalfDegreeQuantizationWrapsAtNorth()
    {
        var north = -(359.8f * MathF.PI / 180f);
        Assert.Equal(0f, PlayerCompassHeading.QuantizedDegrees(north, false));
        var east = -(12.26f * MathF.PI / 180f);
        Assert.Equal(12.5f, PlayerCompassHeading.QuantizedDegrees(east, false));
    }

    [Fact]
    public void ExtraRevolutionsDoNotChangeHeading()
    {
        var yaw = -MathF.PI / 4f;
        var revolutions = yaw - 6f * MathF.PI;
        Assert.Equal(PlayerCompassHeading.QuantizedDegrees(yaw, false),
            PlayerCompassHeading.QuantizedDegrees(revolutions, false));
        Assert.Equal(PlayerCompassHeading.QuantizedDegrees(yaw, true),
            PlayerCompassHeading.QuantizedDegrees(revolutions, true));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void NonFiniteYawIsRejected(float yaw) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerCompassHeading.QuantizedDegrees(yaw, false));
}
