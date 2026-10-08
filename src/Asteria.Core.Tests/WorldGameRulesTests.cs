using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldGameRulesTests
{
    [Fact]
    public void DefaultRulesAndCreationMatchMineClone()
    {
        var rules = new WorldGameRules();
        Assert.Equal(40u, rules.TicksPerSecond);
        Assert.True(rules.SpawnCreatures);

        var options = new WorldCreationOptions("New World", 42);
        Assert.Equal(PlayerGameMode.Survival, options.GameMode);
        Assert.Equal(40u, options.TicksPerSecond);
        Assert.True(options.SpawnCreatures);
    }

    [Fact]
    public void ChangesAreValidatedAndIdempotent()
    {
        var rules = new WorldGameRules(20, false);
        Assert.False(rules.SetTicksPerSecond(20));
        Assert.True(rules.SetTicksPerSecond(80));
        Assert.True(rules.SetSpawnCreatures(true));
        Assert.False(rules.SetSpawnCreatures(true));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => rules.SetTicksPerSecond(0));
        Assert.Equal(80u, rules.TicksPerSecond);
    }

    [Fact]
    public void CreationRejectsInvalidInputs()
    {
        Assert.Throws<ArgumentException>(
            () => new WorldCreationOptions(" ", 42));
        Assert.Throws<ArgumentException>(
            () => new WorldCreationOptions("../world", 42));
        Assert.Throws<ArgumentException>(
            () => new WorldCreationOptions("CON.txt", 42));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldCreationOptions("World", 42, ticksPerSecond: 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldCreationOptions("World", 42, (PlayerGameMode)255));
    }

    [Fact]
    public void GameModeCapabilityContractMatchesMineClone()
    {
        Assert.False(PlayerGameMode.Survival.AllowsFlight());
        Assert.True(PlayerGameMode.Creative.AllowsFlight());
        Assert.True(PlayerGameMode.Creative.HasCreativeInventory());
        Assert.True(PlayerGameMode.Spectator.AllowsFlight());
        Assert.True(PlayerGameMode.Spectator.IsSpectator());
        Assert.False(PlayerGameMode.Spectator.HasCreativeInventory());
    }

    [Fact]
    public void EditedRateUpdatesLogicalClockAndFluidDelay()
    {
        var rules = new WorldGameRules();
        var clock = new WorldTickClock();
        clock.Advance(0.1, rules.TicksPerSecond);
        Assert.Equal(4ul, clock.CurrentTick);
        Assert.Equal(10ul, FluidTiming.DelayTicks(4f, rules.TicksPerSecond));

        rules.SetTicksPerSecond(20);
        clock.Advance(0.1, rules.TicksPerSecond);
        Assert.Equal(6ul, clock.CurrentTick);
        Assert.Equal(5ul, FluidTiming.DelayTicks(4f, rules.TicksPerSecond));
    }

    [Fact]
    public void DistinctSpheresShareRulesWithoutDuplicatingOwnership()
    {
        var definition = new DimensionDefinition(
            DimensionId.Overworld,
            ["asteria:plains"],
            seaLevel: 90,
            18f,
            new DimensionSpawnDefinition(0, 0),
            new DimensionEnvironmentDefinition(
                new DimensionColor(0, 0, 0),
                new DimensionColor(255, 255, 255),
                1f,
                new DimensionColor(0, 0, 0),
                0f));
        var world = new DimensionSessionStateStore(
            new WorldCreationOptions("World", 42, ticksPerSecond: 20),
            new DimensionRegistry([definition]));

        var sphere = world.GetOrCreate(DimensionId.Overworld);
        Assert.Same(world.GameRules, sphere.GameRules);
        Assert.Equal(20u, sphere.GameRules.TicksPerSecond);
        world.GameRules.SetTicksPerSecond(80);
        Assert.Equal(80u, sphere.GameRules.TicksPerSecond);
    }
}
