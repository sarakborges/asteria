using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerHealthTests
{
    [Fact]
    public void SurvivalDamageAndDeathAreAuthoritativeAndIdempotent()
    {
        var player = new PlayerSessionState();
        Assert.Equal(20f, player.Health.Current);
        Assert.Equal(PlayerDamageResult.Ignored,
            player.Health.Damage(12f, PlayerGameMode.Creative));
        Assert.Equal(PlayerDamageResult.Hurt,
            player.Health.Damage(7f, player.GameMode));
        Assert.Equal(13f, player.Health.Current);
        Assert.Equal(PlayerDamageResult.Killed,
            player.Health.Damage(100f, player.GameMode));
        Assert.True(player.Health.IsDead);
        Assert.False(player.CanInteract);
        var revision = player.Health.Revision;
        Assert.Equal(PlayerDamageResult.Ignored,
            player.Health.Damage(1f, player.GameMode));
        Assert.Equal(revision, player.Health.Revision);
        Assert.False(player.Health.Heal(10f));
        Assert.True(player.Health.Respawn());
        Assert.Equal(20f, player.Health.Current);
        Assert.True(player.CanInteract);
        Assert.False(player.Health.Respawn());
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(2.99f, 0f)]
    [InlineData(3f, 0f)]
    [InlineData(3.99f, 0f)]
    [InlineData(4f, 1f)]
    [InlineData(6.8f, 3f)]
    public void FallDamageStartsAfterThreeMeters(float distance, float lost)
    {
        var health = new PlayerHealth();
        health.Land(distance, PlayerGameMode.Survival);
        Assert.Equal(20f - lost, health.Current);
    }

    [Fact]
    public void CreativeSpectatorAndInvalidValuesCannotInflictDamage()
    {
        var health = new PlayerHealth();
        health.Land(100f, PlayerGameMode.Creative);
        health.Land(100f, PlayerGameMode.Spectator);
        Assert.Equal(20f, health.Current);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            health.Damage(float.NaN, PlayerGameMode.Survival));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            health.Land(float.PositiveInfinity, PlayerGameMode.Survival));
        Assert.Throws<InvalidDataException>(() => health.Restore(21f));
        Assert.Throws<InvalidDataException>(() => health.Restore(-1f));
    }

    [Fact]
    public void SnapshotRoundTripsHealthAndRejectsCorruptionBeforeInventoryRestore()
    {
        var source = new PlayerSessionState();
        source.Health.Damage(6f, PlayerGameMode.Survival);
        var snapshot = source.CaptureState();
        var other = new PlayerSessionState(PlayerGameMode.Creative);
        other.RestoreState(snapshot);
        Assert.Equal(14f, other.Health.Current);
        Assert.Equal(PlayerGameMode.Survival, other.GameMode);
        Assert.Throws<InvalidDataException>(() => new PlayerSessionSnapshot(
            PlayerGameMode.Survival, false, source.Inventory.Capture(), float.NaN));
    }
}
