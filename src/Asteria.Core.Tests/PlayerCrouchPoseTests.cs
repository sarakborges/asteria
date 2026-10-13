using Asteria.Core.Content;

namespace Asteria.Core.Tests;

public sealed class PlayerCrouchPoseTests
{
    [Theory]
    [InlineData("BodyPivot", 0.18f)]
    [InlineData("HeadPivot", 0.28f)]
    [InlineData("RightArmPivot", 0.24f)]
    [InlineData("LeftArmPivot", 0.24f)]
    [InlineData("RightLegPivot", 0.02f)]
    [InlineData("LeftLegPivot", 0.02f)]
    public void EveryAnimatedJointMatchesMineCloneCrouchDrop(
        string pivot, float expected)
    {
        Assert.Equal(expected, PlayerCrouchPose.VerticalDrop(pivot, 1f));
        Assert.Equal(expected * 0.5f,
            PlayerCrouchPose.VerticalDrop(pivot, 0.5f));
        Assert.Equal(0f, PlayerCrouchPose.VerticalDrop(pivot, 0f));
    }

    [Fact]
    public void ReturningToStandingRestoresOriginalPivotPosition()
    {
        const float originalY = 1.35f;
        var crouchedY = originalY -
            PlayerCrouchPose.VerticalDrop("HeadPivot", 1f);
        Assert.Equal(1.07f, crouchedY, 3);
        var restoredY = originalY -
            PlayerCrouchPose.VerticalDrop("HeadPivot", 0f);
        Assert.Equal(originalY, restoredY);
    }

    [Fact]
    public void UnknownPartCannotShiftAndBlendIsBounded()
    {
        Assert.Equal(0f, PlayerCrouchPose.VerticalDrop("Visual", 1f));
        Assert.Equal(0f, PlayerCrouchPose.VerticalDrop("HairLayer", 1f));
        Assert.Equal(0.18f, PlayerCrouchPose.VerticalDrop("BodyPivot", 12f));
        Assert.Equal(0f, PlayerCrouchPose.VerticalDrop("BodyPivot", -12f));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerCrouchPose.VerticalDrop("HeadPivot", float.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerCrouchPose.VerticalDrop("HeadPivot", float.PositiveInfinity));
    }
}
