using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FrameWorkBudgetTests
{
    [Theory]
    [InlineData(1.0 / 90.0, 4)]
    [InlineData(1.0 / 60.0, 3)]
    [InlineData(1.0 / 50.0, 2)]
    public void WorldBudgetPolicyMatchesFramePressure(
        double frameSeconds,
        double expectedMilliseconds)
    {
        Assert.Equal(
            expectedMilliseconds,
            WorldFrameWorkBudget
                .DurationForFrameSeconds(frameSeconds)
                .TotalMilliseconds);
    }

    [Fact]
    public void PhaseBudgetCanShortenButNeverExtendGlobalDeadline()
    {
        var global = new WorldFrameWorkBudget(1_100);
        var shortPhase = global.LimitTo(
            TimeSpan.FromMilliseconds(2), nowTimestamp: 1_000,
            timestampFrequency: 1_000);
        var longPhase = new WorldFrameWorkBudget(1_010).LimitTo(
            TimeSpan.FromMilliseconds(20), nowTimestamp: 1_000,
            timestampFrequency: 1_000);

        Assert.Equal(1_002, shortPhase.DeadlineTimestamp);
        Assert.Equal(1_010, longPhase.DeadlineTimestamp);
    }

    [Fact]
    public void MinimumWorkRunsBeforeDeadlineCanExhaustBudget()
    {
        var budget =
            FrameWorkBudget.Begin(
                TimeSpan.FromMilliseconds(1),
                minimumItems: 2,
                nowTimestamp: 0,
                timestampFrequency: 1_000,
                maximumItems: 8);

        Assert.False(
            budget.Exhausted(10_000));

        budget.Record();
        Assert.False(
            budget.Exhausted(10_000));

        budget.Record();
        Assert.True(
            budget.Exhausted(10_000));
    }

    [Fact]
    public void MaximumItemsStopsWorkEvenBeforeTimeDeadline()
    {
        var budget =
            FrameWorkBudget.Begin(
                TimeSpan.FromSeconds(1),
                minimumItems: 0,
                nowTimestamp: 0,
                timestampFrequency: 1_000,
                maximumItems: 2);

        budget.Record(2);

        Assert.True(
            budget.Exhausted(0));
    }

    [Fact]
    public void NearCollisionPrewarmMakesOneProgressEvenPastGlobalDeadline()
    {
        // A local collision prewarm pass cannot be starved forever by
        // chunk streaming spending the global frame budget first.
        var work = FrameWorkBudget.Begin(
            TimeSpan.FromMilliseconds(1),
            minimumItems: 1,
            nowTimestamp: 100,
            timestampFrequency: 1_000,
            maximumItems: 2,
            globalDeadlineTimestamp: 100);

        Assert.False(work.Exhausted(100));
        work.Record();
        Assert.True(work.Exhausted(100));
        Assert.Equal(1, work.ProcessedItems);
    }

    [Fact]
    public void CollisionPrewarmCanUseTwoSlotsButNeverExceedCap()
    {
        var work = FrameWorkBudget.Begin(
            TimeSpan.FromSeconds(1),
            minimumItems: 1,
            nowTimestamp: 0,
            timestampFrequency: 1_000,
            maximumItems: 2,
            globalDeadlineTimestamp: 500);
        Assert.False(work.Exhausted(0));
        work.Record();
        Assert.False(work.Exhausted(1));
        work.Record();
        Assert.True(work.Exhausted(1));
        Assert.Equal(2, work.ProcessedItems);
    }

    [Fact]
    public void GlobalDeadlineCanBoundLocalBudget()
    {
        var budget =
            FrameWorkBudget.Begin(
                TimeSpan.FromSeconds(10),
                minimumItems: 1,
                nowTimestamp: 0,
                timestampFrequency: 1_000,
                maximumItems: 10,
                globalDeadlineTimestamp: 50);

        budget.Record();

        Assert.False(
            budget.Exhausted(49));
        Assert.True(
            budget.Exhausted(50));
    }
}
