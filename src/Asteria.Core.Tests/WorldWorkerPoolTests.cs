using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldWorkerPoolTests
{
    [Fact]
    public void ConstructorRequiresPositiveWorkerCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new WorldWorkerPool(0));
    }

    [Fact]
    public void LargePriorityQueueDoesNotIncreasePhysicalWorkerConcurrency()
    {
        const int workerCount = 2;
        const int queuedOperations = 256;
        using var pool = new WorldWorkerPool(workerCount);
        using var releaseWorkers = new ManualResetEventSlim(false);
        using var bothWorkersStarted = new CountdownEvent(workerCount);
        var active = 0;
        var maximumActive = 0;
        var operations = new List<WorldWorkerOperation<int>>();

        for (var index = 0; index < workerCount; index++)
        {
            var value = index;
            Assert.True(pool.TrySchedule(
                _ =>
                {
                    var currentActive = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, currentActive);
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

        for (var index = 0; index < queuedOperations; index++)
        {
            var value = index + workerCount;
            Assert.True(pool.TrySchedule(
                _ =>
                {
                    var currentActive = Interlocked.Increment(ref active);
                    UpdateMaximum(ref maximumActive, currentActive);
                    Interlocked.Decrement(ref active);
                    return value;
                },
                CancellationToken.None,
                out var operation));
            operations.Add(operation!);
        }

        Assert.Equal(workerCount, pool.WorkerCount);
        Assert.Equal(workerCount, pool.ActiveWorkerCount);
        Assert.Equal(queuedOperations, pool.PendingWorkCount);
        Assert.Equal(workerCount + queuedOperations, pool.InFlightWorkCount);
        Assert.Equal(0, pool.RejectedWorkCount);

        releaseWorkers.Set();
        Assert.True(SpinWait.SpinUntil(
            () => operations.All(operation => operation.IsCompleted) &&
                  pool.InFlightWorkCount == 0,
            TimeSpan.FromSeconds(10)));

        Assert.Equal(workerCount, maximumActive);
        Assert.Equal(
            Enumerable.Range(0, workerCount + queuedOperations),
            operations.Select(GetResult).Order());
        Assert.Equal(workerCount + queuedOperations, pool.CompletedWorkCount);
    }

    [Fact]
    public void QueuedWorkUsesUrgencyThenStableSubmissionOrder()
    {
        using var pool = new WorldWorkerPool(workerCount: 1);
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
    public void StreamingQueueUsesDistanceAndCoordinatesAfterTicketBand()
    {
        using var pool = new WorldWorkerPool(workerCount: 1);
        using var workerStarted = new ManualResetEventSlim(false);
        using var releaseWorker = new ManualResetEventSlim(false);
        var executionOrder = new System.Collections.Concurrent.ConcurrentQueue<string>();

        Assert.True(pool.TrySchedule(
            _ =>
            {
                workerStarted.Set();
                releaseWorker.Wait();
                executionOrder.Enqueue("blocker");
                return "blocker";
            },
            CancellationToken.None,
            out var blocker,
            priority: WorldWorkerPool.BackgroundWorldPriority));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        var far = Schedule(
            "far",
            new ChunkStreamingPriority(1, 4, 4, 0, 0, 0));
        var nearHigherTotal = Schedule(
            "near-higher-total",
            new ChunkStreamingPriority(1, 1, 3, 0, 0, 5));
        var nearCoordinateHigh = Schedule(
            "near-coordinate-high",
            new ChunkStreamingPriority(1, 1, 2, 0, 0, 5));
        var nearCoordinateLow = Schedule(
            "near-coordinate-low",
            new ChunkStreamingPriority(1, 1, 2, 0, 0, 1));
        var exactTieFifo = Schedule(
            "near-coordinate-low-fifo",
            new ChunkStreamingPriority(1, 1, 2, 0, 0, 1));

        releaseWorker.Set();
        var all = new[]
        {
            blocker!, far, nearHigherTotal, nearCoordinateHigh,
            nearCoordinateLow, exactTieFifo,
        };
        Assert.True(SpinWait.SpinUntil(
            () => all.All(operation => operation.IsCompleted) &&
                  pool.InFlightWorkCount == 0,
            TimeSpan.FromSeconds(5)));

        Assert.Equal(
            new[]
            {
                "blocker",
                "near-coordinate-low",
                "near-coordinate-low-fifo",
                "near-coordinate-high",
                "near-higher-total",
                "far",
            },
            executionOrder.ToArray());

        WorldWorkerOperation<string> Schedule(
            string label,
            ChunkStreamingPriority ticket)
        {
            Assert.True(pool.TrySchedule(
                _ =>
                {
                    executionOrder.Enqueue(label);
                    return label;
                },
                CancellationToken.None,
                out var operation,
                WorldWorkerPriority.FromStreaming(ticket)));
            return operation!;
        }
    }

    [Fact]
    public void ReprioritizationReplacesTheEntireStreamingTicketTuple()
    {
        using var pool = new WorldWorkerPool(workerCount: 1);
        using var workerStarted = new ManualResetEventSlim(false);
        using var releaseWorker = new ManualResetEventSlim(false);
        var executionOrder = new System.Collections.Concurrent.ConcurrentQueue<string>();

        Assert.True(pool.TrySchedule(
            _ =>
            {
                workerStarted.Set();
                releaseWorker.Wait();
                executionOrder.Enqueue("blocker");
                return "blocker";
            },
            CancellationToken.None,
            out var blocker,
            priority: WorldWorkerPool.BackgroundWorldPriority));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        var movingChunk = Schedule(
            "moving", new ChunkStreamingPriority(3, 100, 100, 0, 0, 0));
        var nearChunk = Schedule(
            "near", new ChunkStreamingPriority(1, 2, 2, 0, 0, 0));

        Assert.True(pool.TryUpdatePriority(
            movingChunk,
            WorldWorkerPriority.FromStreaming(
                new ChunkStreamingPriority(1, 1, 1, 0, 0, 0))));

        releaseWorker.Set();
        var all = new[] { blocker!, movingChunk, nearChunk };
        Assert.True(SpinWait.SpinUntil(
            () => all.All(operation => operation.IsCompleted) &&
                  pool.InFlightWorkCount == 0,
            TimeSpan.FromSeconds(5)));

        Assert.Equal(
            new[] { "blocker", "moving", "near" },
            executionOrder.ToArray());

        WorldWorkerOperation<string> Schedule(
            string label,
            ChunkStreamingPriority ticket)
        {
            Assert.True(pool.TrySchedule(
                _ =>
                {
                    executionOrder.Enqueue(label);
                    return label;
                },
                CancellationToken.None,
                out var operation,
                WorldWorkerPriority.FromStreaming(ticket)));
            return operation!;
        }
    }

    [Fact]
    public void QueuedOperationCanBeResortedWhenItsOwnerPriorityChanges()
    {
        using var pool = new WorldWorkerPool(workerCount: 1);
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

        var movedChunk = Schedule(40, 1);
        var nearbyChunk = Schedule(32, 2);
        var distantChunk = Schedule(45, 3);

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
        var pool = new WorldWorkerPool(workerCount: 1);
        var completed = 0;

        for (var index = 0; index < 128; index++)
        {
            var value = index;
            Assert.True(pool.TrySchedule(
                _ =>
                {
                    Interlocked.Increment(ref completed);
                    return value;
                },
                CancellationToken.None,
                out _));
        }

        pool.Dispose();

        Assert.Equal(128, completed);
        Assert.Equal(128, pool.CompletedWorkCount);
        Assert.Equal(0, pool.ActiveWorkerCount);
        Assert.Equal(0, pool.PendingWorkCount);
        Assert.False(pool.TrySchedule(
            _ => 129,
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
