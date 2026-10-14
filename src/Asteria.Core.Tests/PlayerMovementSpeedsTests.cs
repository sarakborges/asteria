using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerMovementSpeedsTests
{
    [Fact]
    public void MineCloneGroundAndFlightSpeedsMatchAuthoredConstants()
    {
        Assert.Equal(5f, PlayerMovementSpeeds.Walk);
        Assert.Equal(7.5f, PlayerMovementSpeeds.Run);
        Assert.Equal(25f, PlayerMovementSpeeds.Flight);
        Assert.Equal(7f, PlayerMovementSpeeds.Jump);
        Assert.Equal(1.5f, PlayerGroundMovement.RunSpeedMultiplier);
        Assert.Equal(0.3f, PlayerGroundMovement.CrouchSpeedMultiplier);
        Assert.Equal(28f, PlayerMovementSpeeds.WalkAcceleration);
        Assert.Equal(36f, PlayerMovementSpeeds.WalkDeceleration);
        Assert.Equal(75f, PlayerMovementSpeeds.FlightAcceleration);
        Assert.Equal(90f, PlayerMovementSpeeds.FlightDeceleration);
    }

    [Fact]
    public void FlightAcceleratesAndDeceleratesInThreeDimensions()
    {
        var target = new Vector3(25f, 25f, 0f);
        var first = PlayerMovementPhysics.FlightVelocity(Vector3.Zero, target, 0.1f);
        Assert.Equal(7.5f, first.Length(), 4);
        Assert.Equal(first.X, first.Y);
        var braking = PlayerMovementPhysics.FlightVelocity(
            new Vector3(20f, 0f, 0f), Vector3.Zero, 0.1f);
        Assert.Equal(11f, braking.X, 4);
        Assert.Equal(Vector3.Zero,
            PlayerMovementPhysics.FlightVelocity(braking, Vector3.Zero, 1f));
    }

    [Fact]
    public void FlightMotionIsStableAcrossSubstepsAndReversals()
    {
        var target = new Vector3(25f, 0f, 0f);
        var one = PlayerMovementPhysics.FlightVelocity(Vector3.Zero, target, 0.2f);
        var two = PlayerMovementPhysics.FlightVelocity(
            PlayerMovementPhysics.FlightVelocity(Vector3.Zero, target, 0.1f),
            target, 0.1f);
        Assert.Equal(one, two);
        Assert.Equal(15f, one.X, 4);
        var reverse = PlayerMovementPhysics.FlightVelocity(one, -target, 0.1f);
        Assert.Equal(7.5f, reverse.X, 4);
    }

    [Fact]
    public void FallingSpeedCapsAtMineCloneFlightSpeed()
    {
        Assert.Equal(-25f,
            PlayerMovementPhysics.ApplyGravity(-24f, 18f, 0.2f));
        Assert.Equal(5.2f,
            PlayerMovementPhysics.ApplyGravity(7f, 18f, 0.1f), 4);
        Assert.Equal(-25f,
            PlayerMovementPhysics.ApplyGravity(-100f, 18f, 0f));
    }
}
