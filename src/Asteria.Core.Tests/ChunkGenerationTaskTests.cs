using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationTaskTests
{
    [Fact]
    public void FullTargetBeginsWithLoadingPyramidAndItsNineColumnEnvelope()
    {
        var manager = new ChunkDistanceManager();
        var center = new ChunkColumnCoord(4, -8);
        var scheduled = new List<ScheduledStep>();
        var task = CreateTask(
            manager,
            center,
            new Dictionary<ChunkColumnCoord, ChunkStatus>(),
            scheduled);

        var advance = task.Advance([], CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForFutures, advance.Kind);
        Assert.Equal(9, advance.FutureOperations.Length);
        Assert.Equal(ChunkStatus.Empty, task.ScheduledStatus);
        Assert.False(task.NeedsGeneration);
        Assert.All(
            scheduled,
            step => Assert.Same(
                ChunkPyramid.Loading.GetStep(ChunkStatus.Empty),
                step.Step));
        Assert.Equal(1, manager.GetHolder(center)!.DependencyClaimCount);
        task.Dispose();
        Assert.Equal(0, manager.GetHolder(center)!.DependencyClaimCount);
    }

    [Fact]
    public void MissingPersistedDataExpandsToGenerationEnvelopeThenRunsGenerationSteps()
    {
        var manager = new ChunkDistanceManager();
        var center = new ChunkColumnCoord(-12, 6);
        var persisted = new Dictionary<ChunkColumnCoord, ChunkStatus>();
        var scheduled = new List<ScheduledStep>();
        var task = CreateTask(manager, center, persisted, scheduled);
        var instance = new ChunkGenerationTaskInstance(
            new ChunkGenerationTaskKey(center, ChunkGenerationStepId.SectionReady),
            1);

        var first = task.Advance([], CancellationToken.None);
        Assert.Equal(9, first.FutureOperations.Length);
        var second = task.Advance(
            Complete(first, instance, ChunkStatus.Empty),
            CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForFutures, second.Kind);
        Assert.Equal(529, second.FutureOperations.Length);
        Assert.True(task.NeedsGeneration);
        Assert.All(
            scheduled.Take(9),
            step => Assert.Same(ChunkPyramid.Loading.GetStep(ChunkStatus.Empty), step.Step));
        Assert.All(
            scheduled.Skip(9),
            step => Assert.Same(ChunkPyramid.Loading.GetStep(ChunkStatus.Empty), step.Step));

        var third = task.Advance(
            Complete(second, instance, ChunkStatus.Empty),
            CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.WaitForFutures, third.Kind);
        Assert.Equal(529, third.FutureOperations.Length);
        Assert.Equal(ChunkStatus.StructureStarts, task.ScheduledStatus);
        Assert.All(
            scheduled.Skip(9 + 529),
            step => Assert.Same(
                ChunkPyramid.Generation.GetStep(ChunkStatus.StructureStarts),
                step.Step));

        task.Dispose();
        Assert.All(
            manager.GetHoldersSnapshot(),
            holder => Assert.Equal(0, holder.DependencyClaimCount));
    }

    [Fact]
    public void FailedLayerCancelsTaskAndReleasesEveryClaimExactlyOnce()
    {
        var manager = new ChunkDistanceManager();
        var center = new ChunkColumnCoord(20, 30);
        var scheduled = new List<ScheduledStep>();
        var task = CreateTask(
            manager,
            center,
            new Dictionary<ChunkColumnCoord, ChunkStatus>(),
            scheduled);
        var instance = new ChunkGenerationTaskInstance(
            new ChunkGenerationTaskKey(center, ChunkGenerationStepId.SectionReady),
            1);

        var first = task.Advance([], CancellationToken.None);
        var completions = Complete(first, instance, ChunkStatus.Empty).ToList();
        var failed = first.FutureOperations[0];
        completions[0] = new ChunkGenerationDependencyCompletion(
            instance,
            1,
            failed.DependencyId,
            ChunkStatusFutureResult.Failure(
                ChunkStatus.Empty,
                "Simulated unavailable dependency."));

        var result = task.Advance(completions, CancellationToken.None);

        Assert.Equal(ChunkGenerationTaskAdvanceKind.Completed, result.Kind);
        Assert.All(
            manager.GetHoldersSnapshot(),
            holder => Assert.Equal(0, holder.DependencyClaimCount));
        task.Dispose();
        Assert.All(
            manager.GetHoldersSnapshot(),
            holder => Assert.Equal(0, holder.DependencyClaimCount));
    }

    private static ChunkGenerationTask CreateTask(
        ChunkDistanceManager manager,
        ChunkColumnCoord center,
        Dictionary<ChunkColumnCoord, ChunkStatus> persisted,
        List<ScheduledStep> scheduled) =>
        ChunkGenerationTask.Create(
            manager,
            center,
            ChunkStatus.Full,
            holder => persisted.TryGetValue(holder.Position, out var status)
                ? status
                : null,
            (dependencyId, holder, step, cache) =>
            {
                scheduled.Add(new ScheduledStep(holder.Position, step));
                if (!persisted.TryGetValue(holder.Position, out var current) ||
                    step.TargetStatus.Index > current.Index)
                    persisted[holder.Position] = step.TargetStatus;
                return ChunkGenerationFutureOperation.From(
                    dependencyId,
                    Task.FromResult(
                        ChunkStatusFutureResult.Success(step.TargetStatus)));
            });

    private static IReadOnlyList<ChunkGenerationDependencyCompletion> Complete(
        ChunkGenerationTaskAdvanceResult advance,
        ChunkGenerationTaskInstance instance,
        ChunkStatus status) =>
        advance.FutureOperations
            .Select(operation => new ChunkGenerationDependencyCompletion(
                instance,
                1,
                operation.DependencyId,
                ChunkStatusFutureResult.Success(status)))
            .ToArray();

    private sealed record ScheduledStep(
        ChunkColumnCoord Column,
        ChunkStep Step);
}
