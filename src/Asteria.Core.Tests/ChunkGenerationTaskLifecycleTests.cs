using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationTaskLifecycleTests
{
    [Fact]
    public void TaskRunsThenWaitsAndResumesOnlyAfterEveryDependencyCompletes()
    {
        var lifecycle = new ChunkGenerationTaskLifecycle();

        Assert.Equal(ChunkGenerationTaskState.ReadyToRun, lifecycle.State);
        Assert.True(lifecycle.TryBeginExecution());
        Assert.Equal(ChunkGenerationTaskState.Running, lifecycle.State);

        var waitToken = lifecycle.BeginWaitingForDependencies([11, 12, 13]);
        Assert.Equal(ChunkGenerationTaskState.WaitingForDependencies, lifecycle.State);
        Assert.Equal(3, lifecycle.PendingDependencyCount);
        Assert.Equal(waitToken, lifecycle.CurrentWaitToken);

        Assert.Equal(
            ChunkGenerationDependencySignalResult.StillWaiting,
            lifecycle.SignalDependencyCompleted(waitToken, 12));
        Assert.Equal(
            ChunkGenerationDependencySignalResult.StillWaiting,
            lifecycle.SignalDependencyCompleted(waitToken, 11));
        Assert.Equal(1, lifecycle.PendingDependencyCount);
        Assert.Equal(
            ChunkGenerationDependencySignalResult.ReadyToResume,
            lifecycle.SignalDependencyCompleted(waitToken, 13));

        Assert.Equal(ChunkGenerationTaskState.ReadyToResume, lifecycle.State);
        Assert.Equal(0, lifecycle.PendingDependencyCount);
        Assert.Null(lifecycle.CurrentWaitToken);
        Assert.True(lifecycle.TryBeginExecution());
        Assert.Equal(ChunkGenerationTaskState.Running, lifecycle.State);
    }

    [Fact]
    public void DuplicateDependencyCompletionCannotResumeTaskEarly()
    {
        var lifecycle = RunningTask();
        var waitToken = lifecycle.BeginWaitingForDependencies([21, 22]);

        Assert.Equal(
            ChunkGenerationDependencySignalResult.StillWaiting,
            lifecycle.SignalDependencyCompleted(waitToken, 21));
        Assert.Equal(
            ChunkGenerationDependencySignalResult.DuplicateOrUnknownDependency,
            lifecycle.SignalDependencyCompleted(waitToken, 21));
        Assert.Equal(1, lifecycle.PendingDependencyCount);
        Assert.Equal(ChunkGenerationTaskState.WaitingForDependencies, lifecycle.State);

        Assert.Equal(
            ChunkGenerationDependencySignalResult.ReadyToResume,
            lifecycle.SignalDependencyCompleted(waitToken, 22));
    }

    [Fact]
    public void CompletionFromAnEarlierWaitCannotAffectALaterWait()
    {
        var lifecycle = RunningTask();
        var firstWait = lifecycle.BeginWaitingForDependencies([31]);
        Assert.Equal(
            ChunkGenerationDependencySignalResult.ReadyToResume,
            lifecycle.SignalDependencyCompleted(firstWait, 31));
        Assert.True(lifecycle.TryBeginExecution());

        var secondWait = lifecycle.BeginWaitingForDependencies([32]);
        Assert.NotEqual(firstWait, secondWait);
        Assert.Equal(
            ChunkGenerationDependencySignalResult.StaleWait,
            lifecycle.SignalDependencyCompleted(firstWait, 32));
        Assert.Equal(1, lifecycle.PendingDependencyCount);
        Assert.Equal(ChunkGenerationTaskState.WaitingForDependencies, lifecycle.State);

        Assert.Equal(
            ChunkGenerationDependencySignalResult.ReadyToResume,
            lifecycle.SignalDependencyCompleted(secondWait, 32));
    }

    [Fact]
    public void UnknownDependencyCannotReducePendingCount()
    {
        var lifecycle = RunningTask();
        var waitToken = lifecycle.BeginWaitingForDependencies([41]);

        Assert.Equal(
            ChunkGenerationDependencySignalResult.DuplicateOrUnknownDependency,
            lifecycle.SignalDependencyCompleted(waitToken, 42));
        Assert.Equal(1, lifecycle.PendingDependencyCount);
        Assert.Equal(ChunkGenerationTaskState.WaitingForDependencies, lifecycle.State);
    }

    [Fact]
    public void WaitingRequiresRunningTaskAndNonEmptyUniqueDependencyIds()
    {
        var lifecycle = new ChunkGenerationTaskLifecycle();

        Assert.Throws<InvalidOperationException>(
            () => lifecycle.BeginWaitingForDependencies([1]));
        Assert.True(lifecycle.TryBeginExecution());
        Assert.Throws<ArgumentException>(
            () => lifecycle.BeginWaitingForDependencies([]));
        Assert.Throws<ArgumentException>(
            () => lifecycle.BeginWaitingForDependencies([1, 1]));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => lifecycle.BeginWaitingForDependencies([0]));
        Assert.Equal(ChunkGenerationTaskState.Running, lifecycle.State);
    }

    [Fact]
    public void CancellationInvalidatesOutstandingWaitAndIsTerminal()
    {
        var lifecycle = RunningTask();
        var waitToken = lifecycle.BeginWaitingForDependencies([51, 52]);

        Assert.True(lifecycle.TryCancel());
        Assert.Equal(ChunkGenerationTaskState.Cancelled, lifecycle.State);
        Assert.Equal(0, lifecycle.PendingDependencyCount);
        Assert.Null(lifecycle.CurrentWaitToken);
        Assert.False(lifecycle.TryBeginExecution());
        Assert.False(lifecycle.TryComplete());
        Assert.False(lifecycle.TryCancel());
        Assert.Equal(
            ChunkGenerationDependencySignalResult.StaleWait,
            lifecycle.SignalDependencyCompleted(waitToken, 51));
    }

    [Fact]
    public void FailureIsRetainedAndInvalidatesOutstandingWait()
    {
        var lifecycle = RunningTask();
        var waitToken = lifecycle.BeginWaitingForDependencies([61]);
        var error = new InvalidOperationException("dependency failed");

        Assert.True(lifecycle.TryFail(error));
        Assert.Equal(ChunkGenerationTaskState.Failed, lifecycle.State);
        Assert.Same(error, lifecycle.Failure);
        Assert.False(lifecycle.TryBeginExecution());
        Assert.False(lifecycle.TryComplete());
        Assert.Equal(
            ChunkGenerationDependencySignalResult.StaleWait,
            lifecycle.SignalDependencyCompleted(waitToken, 61));
    }

    [Fact]
    public void CompletionIsTerminalAndCannotBeRepeated()
    {
        var lifecycle = RunningTask();

        Assert.True(lifecycle.TryComplete());
        Assert.Equal(ChunkGenerationTaskState.Completed, lifecycle.State);
        Assert.False(lifecycle.TryComplete());
        Assert.False(lifecycle.TryCancel());
        Assert.False(lifecycle.TryFail(new InvalidOperationException()));
        Assert.False(lifecycle.TryBeginExecution());
    }

    private static ChunkGenerationTaskLifecycle RunningTask()
    {
        var lifecycle = new ChunkGenerationTaskLifecycle();
        Assert.True(lifecycle.TryBeginExecution());
        return lifecycle;
    }
}
