using System.Numerics;
using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WarpArrivalPlanTests
{
    private static WarpArrivalPlan Requested() =>
        new(new DimensionId("asteria:overworld"),
            new Vector3(12.5f, 70f, -20.5f));

    [Fact]
    public void SafeCrossSphereArrivalCanEnterImmediately()
    {
        var plan = Requested();
        Assert.Equal(WarpArrivalDisposition.Enter, plan.Evaluate(true));
        Assert.False(plan.IsReturning);
        Assert.Equal(new DimensionId("asteria:overworld"), plan.Origin);
    }

    [Fact]
    public void BlockedDestinationRetriesSpawnThenRollsBack()
    {
        var plan = Requested();
        Assert.Equal(WarpArrivalDisposition.RetryAtGeneratedSpawn,
            plan.Evaluate(false));
        var retry = plan.RetryAtGeneratedSpawn();
        Assert.Equal(WarpArrivalPhase.DestinationFallback, retry.Phase);
        Assert.Equal(WarpArrivalDisposition.ReturnToOrigin,
            retry.Evaluate(false));
        var returning = retry.BeginReturn();
        Assert.True(returning.IsReturning);
        Assert.Equal(plan.Origin, returning.Origin);
        Assert.Equal(plan.OriginPosition, returning.OriginPosition);
        Assert.Equal(WarpArrivalDisposition.Enter, returning.Evaluate(true));
    }

    [Fact]
    public void BlockedReturnHasOnlyOneAdditionalSpawnAttempt()
    {
        var returning = Requested()
            .RetryAtGeneratedSpawn()
            .BeginReturn();
        Assert.Equal(WarpArrivalDisposition.RetryAtGeneratedSpawn,
            returning.Evaluate(false));
        var final = returning.RetryAtGeneratedSpawn();
        Assert.Equal(WarpArrivalPhase.ReturnFallback, final.Phase);
        Assert.Equal(WarpArrivalDisposition.NoSafeEntry,
            final.Evaluate(false));
        Assert.True(final.IsReturning);
    }

    [Fact]
    public void PhaseTransitionsAreExplicitAndCannotSkipSafetySteps()
    {
        var initial = Requested();
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = initial.BeginReturn();
        });
        var destinationRetry = initial.RetryAtGeneratedSpawn();
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = destinationRetry.RetryAtGeneratedSpawn();
        });
        var returning = destinationRetry.BeginReturn();
        Assert.Throws<InvalidOperationException>(() =>
        {
            _ = returning.BeginReturn();
        });
    }
}
