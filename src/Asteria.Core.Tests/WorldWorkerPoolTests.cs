using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldWorkerPoolTests
{
    [Fact]
    public void ConstructorRequiresPositiveWorkerAndQueueCapacities()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldWorkerPool(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldWorkerPool(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldWorkerPool(1, -1));
    }

    [Fact]
    public void OneExecutorBoundsPhysicalConcurrencyAcrossIndependentOperations()
    {
        using var pool = new WorldWorkerPool(workerCount: 2, pendingCapacity: 2);
        using var releaseWorkers = new ManualResetEventSlim(false);
        using var bothWorkersStarted = new CountdownEvent(2);
        var active = 0;
        var maximumActive = 0;
        var operations = new List<WorldWorkerOperation<int>>();

        for (var index = 0; index < 2; index++)
        {
            var value = index;
            Assert.True(pool.TrySchedule(
                _ =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, current);
                    bothWorkersStarted.Signal();
                    releaseWorkers.Wait();
                    Interlocked.Decrement(ref active);
                    return value;
                },
                CancellationToken.None,
                out var operation));
            operations.Add(operation!);
        }

        Assert.True(bothWorkersStarted.Wait(TimeSpan.FromSeconds(5)));
        for (var index = 2; index < 4; index++)
        {
            var value = index;
            Assert.True(pool.TrySchedule(
                _ =>
                {
                    var current = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, current);
                    releaseWorkers.Wait();
                    Interlocked.Decrement(ref active);
                    return value;
                },
                CancellationToken.None,
                out var operation));
            operations.Add(operation!);
        }

        Assert.Equal(2, pool.WorkerCount);
        Assert.Equal(2, pool.ActiveWorkerCount);
        Assert.Equal(2, pool.PendingWorkCount);
        Assert.Equal(4, pool.InFlightWorkCount);
        Assert.Equal(0, pool.AvailableSubmissionCount);

        Assert.False(pool.TrySchedule(
            _ => 99,
            CancellationToken.None,
            out _));
        Assert.Equal(1, pool.RejectedWorkCount);

        releaseWorkers.Set();
        Assert.True(SpinWait.SpinUntil(
            () => operations.All(operation => operation.IsCompleted) &&
                  pool.InFlightWorkCount == 0,
            TimeSpan.FromSeconds(5)));

        Assert.Equal(2, maximumActive);
        Assert.Equal(new[] { 0, 1, 2, 3 },
            operations.Select(GetResult).Order().ToArray());
        Assert.Equal(4, pool.CompletedWorkCount);
    }

    [Fact]
    public void QueuedWorkUsesUrgencyThenStableSubmissionOrder()
    {
        using var pool = new WorldWorkerPool(workerCount: 1, pendingCapacity: 4);
        using var workerStarted = new ManualResetEventSlim(false);
        using var releaseWorker = new ManualResetEventSlim(false);
        var executionOrder = new System.Collections.Concurrent.ConcurrentQueue<int>();

        Assert.True(pool.TrySchedule(
            _ =>
            {
                workerStarted.Set();
                releaseWorker.Wait();
                executionOrder.Enqueue(-1);
                return -1;
            },
            CancellationToken.None,
            out var blocker,
            priority: WorldWorkerPool.BackgroundWorldPriority));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        var low = Schedule(30, 3);
        var firstHigh = Schedule(0, 0);
        var medium = Schedule(20, 2);
        var secondHigh = Schedule(0, 1);

        Assert.Equal(4, pool.PendingWorkCount);
        Assert.Equal(0, pool.AvailableSubmissionCount);

        releaseWorker.Set();
        var all = new[] { blocker!, low, firstHigh, medium, secondHigh };
        Assert.True(SpinWait.SpinUntil(
            () => all.All(operation => operation.IsCompleted) &&
                  pool.InFlightWorkCount == 0,
            TimeSpan.FromSeconds(5)));

        Assert.Equal(new[] { -1, 0, 1, 2, 3 }, executionOrder.ToArray());
        Assert.Equal(5, pool.CompletedWorkCount);

        WorldWorkerOperation<int> Schedule(int priority, int value)
        {
            Assert.True(pool.TrySchedule(
                _ =>
                {
                    executionOrder.Enqueue(value);
                    return value;
                },
                CancellationToken.None,
                out var operation,
                priority: priority));
            return operation!;
        }
    }

    [Fact]
    public void QueuedOperationCanBeResortedWhenItsOwnerPriorityChanges()
    {
        using var pool = new WorldWorkerPool(workerCount: 1, pendingCapacity: 3);
        using var workerStarted = new ManualResetEventSlim(false);
        using var releaseWorker = new ManualResetEventSlim(false);
        var executionOrder = new System.Collections.Concurrent.ConcurrentQueue<int>();

        Assert.True(pool.TrySchedule(
            _ =>
            {
                workerStarted.Set();
                releaseWorker.Wait();
                executionOrder.Enqueue(-1);
                return -1;
            },
            CancellationToken.None,
            out var running,
            priority: WorldWorkerPool.BackgroundWorldPriority));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        Assert.False(pool.TryUpdatePriority(
            running!, WorldWorkerPool.PlayerChunkPriority));

        var movedChunk = Schedule(30, 1);
        var nearbyChunk = Schedule(20, 2);
        var distantChunk = Schedule(40, 3);

        Assert.True(pool.TryUpdatePriority(
            movedChunk, WorldWorkerPool.PlayerChunkPriority));

        releaseWorker.Set();
        var all = new[] { running!, movedChunk, nearbyChunk, distantChunk };
        Assert.True(SpinWait.SpinUntil(
            () => all.All(operation => operation.IsCompleted) &&
                  pool.InFlightWorkCount == 0,
            TimeSpan.FromSeconds(5)));

        Assert.Equal(new[] { -1, 1, 2, 3 }, executionOrder.ToArray());
        Assert.False(pool.TryUpdatePriority(
            movedChunk, WorldWorkerPool.BackgroundWorldPriority));

        WorldWorkerOperation<int> Schedule(int priority, int value)
        {
            Assert.True(pool.TrySchedule(
                _ =>
                {
                    executionOrder.Enqueue(value);
                    return value;
                },
                CancellationToken.None,
                out var operation,
                priority: priority));
            return operation!;
        }
    }

    [Fact]
    public void CancellationAndFailuresArePublishedAsOperationResults()
    {
        using var pool = new WorldWorkerPool(workerCount: 1);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var expectedError = new InvalidOperationException("worker failed");

        Assert.True(pool.TrySchedule<int>(
            _ => throw expectedError,
            CancellationToken.None,
            out var failed));
        Assert.True(pool.TrySchedule(
            _ => 42,
            cancellation.Token,
            out var cancelled));

        Assert.True(SpinWait.SpinUntil(
            () => failed!.IsCompleted && cancelled!.IsCompleted,
            TimeSpan.FromSeconds(5)));

        Assert.True(failed!.TryGetCompletion(out var failedResult));
        Assert.Same(expectedError, failedResult.Error);
        Assert.True(cancelled!.TryGetCompletion(out var cancelledResult));
        Assert.IsType<OperationCanceledException>(cancelledResult.Error);
        Assert.Equal(1, pool.FailedWorkCount);
        Assert.Equal(1, pool.CancelledWorkCount);
    }

    [Fact]
    public void CompletionCallbackFailureDoesNotTerminateAWorker()
    {
        using var pool = new WorldWorkerPool(workerCount: 1);
        Assert.True(pool.TrySchedule(
            _ => 1,
            CancellationToken.None,
            out var first,
            onCompleted: _ => throw new InvalidOperationException("callback failed")));
        Assert.True(SpinWait.SpinUntil(
            () => first!.IsCompleted && pool.InFlightWorkCount == 0,
            TimeSpan.FromSeconds(5)));

        Assert.True(pool.TrySchedule(
            _ => 2,
            CancellationToken.None,
            out var second));
        Assert.True(SpinWait.SpinUntil(
            () => second!.IsCompleted,
            TimeSpan.FromSeconds(5)));

        Assert.True(second!.TryGetCompletion(out var completion));
        Assert.Equal(2, completion.Result);
        Assert.Equal(1, pool.CompletionCallbackFailures);
        Assert.Equal(2, pool.CompletedWorkCount);
    }

    [Fact]
    public void DisposeDrainsQueuedOperationsBeforeWorkerThreadsExit()
    {
        var pool = new WorldWorkerPool(workerCount: 1, pendingCapacity: 3);
        var completed = 0;

        for (var index = 0; index < 3; index++)
        {
            Assert.True(pool.TrySchedule(
                _ =>
                {
                    Interlocked.Increment(ref completed);
                    return index;
                },
                CancellationToken.None,
                out _));
        }

        pool.Dispose();

        Assert.Equal(3, completed);
        Assert.Equal(3, pool.CompletedWorkCount);
        Assert.Equal(0, pool.ActiveWorkerCount);
        Assert.Equal(0, pool.PendingWorkCount);
        Assert.False(pool.TrySchedule(
            _ => 4,
            CancellationToken.None,
            out _));
        Assert.Equal(1, pool.RejectedWorkCount);
    }

    private static int GetResult(WorldWorkerOperation<int> operation)
    {
        Assert.True(operation.TryGetCompletion(out var completion));
        Assert.Null(completion.Error);
        return completion.Result;
    }

    private static void UpdateMaximum(ref int maximum, int value)
    {
        var observed = Volatile.Read(ref maximum);
        while (value > observed)
        {
            var previous = Interlocked.CompareExchange(
                ref maximum, value, observed);
            if (previous == observed)
                return;
            observed = previous;
        }
    }
}
