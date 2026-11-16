using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ProximityDispatchFairnessTests
{
    [Fact]
    public void EveryEighthSuccessfulDispatchUsesFairFallback()
    {
        var fairness = new ProximityDispatchFairness();
        for (var i = 0; i < 7; i++)
        {
            Assert.True(fairness.ShouldPrioritizeNearby(true));
            fairness.RecordStarted(prioritizedNearby: true);
        }

        Assert.False(fairness.ShouldPrioritizeNearby(true));
        // An unsuccessful worker start must not advance or reset the policy.
        Assert.False(fairness.ShouldPrioritizeNearby(true));
        fairness.RecordStarted(prioritizedNearby: false);
        Assert.True(fairness.ShouldPrioritizeNearby(true));
    }

    [Fact]
    public void MissingFocusUsesOrdinaryDispatch()
    {
        var fairness = new ProximityDispatchFairness();
        Assert.False(fairness.ShouldPrioritizeNearby(false));
        fairness.RecordStarted(prioritizedNearby: false);
        Assert.True(fairness.ShouldPrioritizeNearby(true));
    }
}
