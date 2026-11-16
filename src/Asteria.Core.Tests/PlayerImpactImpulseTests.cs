using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerImpactImpulseTests
{
    [Fact]
    public void LandingStrikePushesAwayFromCreatureAndPreservesWorldAxes()
    {
        var knockback = PlayerImpactImpulse.FromMelee(
            new Vector3(5f, 15f, 3f),
            new Vector3(2f, 1f, 7f));
        Assert.Equal(-3.6f, knockback.X, 4);
        Assert.Equal(4.8f, knockback.Z, 4);
        Assert.Equal(PlayerImpactImpulse.MeleeVerticalSpeed, knockback.Y);
        Assert.Equal(PlayerImpactImpulse.MeleeHorizontalSpeed,
            new Vector2(knockback.X, knockback.Z).Length(), 4);
    }

    [Fact]
    public void ProjectilePushesAwayFromItsActualImpactNotAStaleCreaturePosition()
    {
        var fromLeft = PlayerImpactImpulse.FromProjectile(
            new Vector3(-1f, 2f, 0f), Vector3.Zero);
        var fromRight = PlayerImpactImpulse.FromProjectile(
            new Vector3(1f, 2f, 0f), Vector3.Zero);
        Assert.Equal(PlayerImpactImpulse.ProjectileHorizontalSpeed,
            fromLeft.X);
        Assert.Equal(-PlayerImpactImpulse.ProjectileHorizontalSpeed,
            fromRight.X);
        Assert.Equal(PlayerImpactImpulse.ProjectileVerticalSpeed, fromLeft.Y);
        Assert.Equal(0f, fromLeft.Z);
    }

    [Fact]
    public void ExactOverlapUsesDeterministicFiniteFallback()
    {
        var origin = new Vector3(3f, 5f, 8f);
        var first = PlayerImpactImpulse.FromMelee(origin, origin);
        Assert.Equal(first, PlayerImpactImpulse.FromMelee(origin, origin));
        Assert.Equal(PlayerImpactImpulse.MeleeHorizontalSpeed, first.X);
        Assert.Equal(0f, first.Z);
        Assert.All(new[] { first.X, first.Y, first.Z }, value =>
            Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void NonfiniteWorldCoordinatesNeverCreateAnInvalidPhysicsVelocity()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerImpactImpulse.FromMelee(
                new Vector3(float.NaN, 0f, 0f), Vector3.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PlayerImpactImpulse.FromProjectile(
                Vector3.Zero, new Vector3(0f, 0f, float.PositiveInfinity)));
    }
}
