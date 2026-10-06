using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class BlockUvRotationTests
{
    [Fact]
    public void AxisOrientationAlwaysCompensatesUvEvenWithoutAuthoredRotation()
    {
        Assert.Equal(
            TextureRotation.Degrees90,
            BlockUvRotation.ForWorldFace(
                BlockFace.Right,
                BlockOrientation.Z,
                TextureRotation.Degrees270,
                usesAuthoredRotation: false));

        Assert.Equal(
            TextureRotation.Degrees270,
            BlockUvRotation.ForWorldFace(
                BlockFace.Front,
                BlockOrientation.X,
                TextureRotation.Degrees180,
                usesAuthoredRotation: false));
    }

    [Fact]
    public void AuthoredRotationComposesAfterOrientationCompensation()
    {
        Assert.Equal(
            TextureRotation.Degrees180,
            BlockUvRotation.ForWorldFace(
                BlockFace.Right,
                BlockOrientation.Z,
                TextureRotation.Degrees90,
                usesAuthoredRotation: true));

        Assert.Equal(
            TextureRotation.Degrees0,
            BlockUvRotation.ForWorldFace(
                BlockFace.Front,
                BlockOrientation.X,
                TextureRotation.Degrees90,
                usesAuthoredRotation: true));
    }

    [Fact]
    public void YOrientationOnlyUsesAuthoredRotationWhenEnabled()
    {
        Assert.Equal(
            TextureRotation.Degrees0,
            BlockUvRotation.ForWorldFace(
                BlockFace.Top,
                BlockOrientation.Y,
                TextureRotation.Degrees270,
                usesAuthoredRotation: false));

        Assert.Equal(
            TextureRotation.Degrees270,
            BlockUvRotation.ForWorldFace(
                BlockFace.Top,
                BlockOrientation.Y,
                TextureRotation.Degrees270,
                usesAuthoredRotation: true));
    }
}
