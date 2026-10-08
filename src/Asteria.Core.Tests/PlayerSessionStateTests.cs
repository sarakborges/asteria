using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class PlayerSessionStateTests
{
    [Fact]
    public void DefaultsAndModeTransitionsResetFlying()
    {
        var player = new PlayerSessionState();
        Assert.Equal(PlayerGameMode.Survival, player.GameMode);
        Assert.True(player.CanInteract);
        Assert.False(player.IsFlying);
        Assert.False(player.SetGameMode(PlayerGameMode.Survival));
        Assert.True(player.SetGameMode(PlayerGameMode.Spectator));
        Assert.True(player.IsFlying);
        Assert.False(player.CanInteract);
        Assert.True(player.SetGameMode(PlayerGameMode.Creative));
        Assert.False(player.IsFlying);
        Assert.True(player.CanInteract);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => player.SetGameMode((PlayerGameMode)255));
    }

    [Fact]
    public void CreativeFlightTogglesOnlyOnSecondJumpWithinTickWindow()
    {
        var player = new PlayerSessionState(PlayerGameMode.Creative);
        Assert.False(player.JumpPressed(100));
        Assert.False(player.IsFlying);
        Assert.True(player.JumpPressed(112));
        Assert.True(player.IsFlying);
        Assert.False(player.JumpPressed(200));
        Assert.False(player.JumpPressed(213));
        Assert.True(player.JumpPressed(214));
        Assert.False(player.IsFlying);
        Assert.False(player.JumpPressed(220));
        player.CancelDoubleTap();
        Assert.False(player.JumpPressed(221));
        Assert.False(player.IsFlying);
    }

    [Fact]
    public void FlightRulesRespectSurvivalSpectatorAndLanding()
    {
        var player = new PlayerSessionState();
        Assert.False(player.JumpPressed(0));
        player.SetGameMode(PlayerGameMode.Spectator);
        Assert.False(player.JumpPressed(1));
        Assert.True(player.IsFlying);
        Assert.False(player.Land());
        player.SetGameMode(PlayerGameMode.Creative);
        player.JumpPressed(3);
        player.JumpPressed(4);
        Assert.True(player.Land());
        Assert.False(player.IsFlying);
        Assert.False(player.Land());
        player.SetGameMode(PlayerGameMode.Survival);
        Assert.False(player.JumpPressed(5));
    }

    [Fact]
    public void SessionStoreOwnsOneModeAcrossAllSphereStates()
    {
        var dimensions = new DimensionRegistry([
            Create("asteria:overworld"),
            Create("asteria:umbral"),
        ]);
        var store = new DimensionSessionStateStore(
            new WorldCreationOptions(
                "Example", 42, PlayerGameMode.Creative),
            dimensions);
        var first = store.GetOrCreate(
            new DimensionId("asteria:overworld"));
        var second = store.GetOrCreate(
            new DimensionId("asteria:umbral"));
        Assert.NotSame(first, second);
        Assert.Equal(PlayerGameMode.Creative, store.Player.GameMode);
        store.Player.SetGameMode(PlayerGameMode.Spectator);
        Assert.Equal(PlayerGameMode.Spectator, store.Player.GameMode);
    }

    private static DimensionDefinition Create(string id) =>
        new(
            new DimensionId(id),
            [id + "/plains"],
            seaLevel: 90,
            18f,
            new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255),
                1f,
                new DimensionColor(0, 0, 0),
                0f));
}
