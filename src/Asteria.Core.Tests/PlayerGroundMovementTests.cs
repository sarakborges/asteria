using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerGroundMovementTests
{
    [Fact]
    public void DoubleTapForwardWithinWindowEnablesRunUntilRelease()
    {
        var state = new PlayerGroundMovement();
        state.ForwardChanged(true, 100);
        Assert.False(state.IsRunning);
        state.ForwardChanged(true, 101); // key-repeat must not count
        Assert.False(state.IsRunning);
        state.ForwardChanged(false, 103);
        state.ForwardChanged(true, 112);
        Assert.True(state.IsRunning);
        Assert.Equal(1.5f, state.SpeedMultiplier(false, false));
        state.ForwardChanged(false, 113);
        Assert.False(state.IsRunning);
        Assert.Equal(1f, state.SpeedMultiplier(false, false));
    }

    [Fact]
    public void ExpiredWindowCannotActivateRunUntilAnotherTap()
    {
        var state = new PlayerGroundMovement();
        state.ForwardChanged(true, 20);
        state.ForwardChanged(false, 21);
        state.ForwardChanged(true, 33);
        Assert.False(state.IsRunning);
        state.ForwardChanged(false, 34);
        state.ForwardChanged(true, 45);
        Assert.True(state.IsRunning);
    }

    [Fact]
    public void CrouchOverridesRunningAndCannotStandThroughCeiling()
    {
        var state = new PlayerGroundMovement();
        state.ForwardChanged(true, 10);
        state.ForwardChanged(false, 11);
        state.ForwardChanged(true, 12);
        Assert.True(state.IsRunning);

        Assert.True(state.UpdateCrouch(true, true));
        Assert.True(state.IsCrouching);
        Assert.False(state.IsRunning);
        Assert.Equal(0.3f, state.SpeedMultiplier(false, false));
        Assert.False(state.UpdateCrouch(false, false));
        Assert.True(state.IsCrouching);
        Assert.True(state.UpdateCrouch(false, true));
        Assert.False(state.IsCrouching);
        Assert.Equal(1f, state.SpeedMultiplier(false, false));
    }

    [Fact]
    public void FlyingAndSwimmingIgnoreGroundSpeedMultipliers()
    {
        var state = new PlayerGroundMovement();
        state.UpdateCrouch(true, true);
        Assert.Equal(1f, state.SpeedMultiplier(flying: true, immersed: false));
        Assert.Equal(1f, state.SpeedMultiplier(flying: false, immersed: true));
        state.Reset();
        Assert.False(state.IsCrouching);
        Assert.False(state.IsRunning);
        state.ForwardChanged(true, 2);
        state.ForwardChanged(false, 3);
        state.ForwardChanged(true, 4);
        Assert.True(state.IsRunning);
        state.Reset();
        Assert.False(state.IsRunning);
    }

    [Fact]
    public void ReleasingGameplayInputPreservesSafeCrouchingPose()
    {
        var state = new PlayerGroundMovement();
        state.UpdateCrouch(true, true);
        state.ForwardChanged(true, 10);
        state.ReleaseInput();
        Assert.True(state.IsCrouching);
        Assert.False(state.IsRunning);
        Assert.False(state.UpdateCrouch(false, false));
        Assert.True(state.UpdateCrouch(false, true));
        Assert.False(state.IsCrouching);
    }

    [Fact]
    public void SaturatedClockDoesNotOverflowDoubleTap()
    {
        var state = new PlayerGroundMovement();
        state.ForwardChanged(true, ulong.MaxValue - 1);
        state.ForwardChanged(false, ulong.MaxValue - 1);
        state.ForwardChanged(true, ulong.MaxValue);
        Assert.True(state.IsRunning);
    }
}
