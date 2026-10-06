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
