using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationWorkerPoolTests
{
    [Fact]
    public void ConstructorRequiresPositiveWorkerAndQueueCapacities()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(4);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChunkGenerationWorkerPool(0, 1, mailbox));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChunkGenerationWorkerPool(1, 0, mailbox));
    }

    [Fact]
    public async Task WorkerPublishesDetachedResultToCompletionMailbox()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(2);
        await using var pool = new ChunkGenerationWorkerPool(1, 1, mailbox);
        var result = new WorkerResult(64, "terrain-layer");

        Assert.True(pool.TrySchedule(Request(
            dependencyId: 1,
            _ => result)));
        ChunkGenerationDependencyCompletion completion = default;
        Assert.True(SpinWait.SpinUntil(
            () => mailbox.TryDequeue(out completion),
            TimeSpan.FromSeconds(5)));

        Assert.Equal(1UL, completion.DependencyId);
        Assert.Same(result, completion.Result);
        Assert.Null(completion.Error);
        Assert.True(SpinWait.SpinUntil(
            () => pool.CompletedWorkCount == 1,
            TimeSpan.FromSeconds(5)));
        Assert.Equal(0, pool.ActiveWorkerCount);
        Assert.Equal(0, pool.InFlightWorkCount);
    }

    [Fact]
    public async Task QueueAndWorkerAdmissionAreBoundedWithoutBlockingCaller()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(4);
        await using var pool = new ChunkGenerationWorkerPool(1, 1, mailbox);
        using var releaseWorker = new ManualResetEventSlim(false);
        using var workerStarted = new ManualResetEventSlim(false);

        Assert.True(pool.TrySchedule(Request(1, _ =>
        {
            workerStarted.Set();
            releaseWorker.Wait();
            return new WorkerResult(1, "first");
        })));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        Assert.True(pool.TrySchedule(Request(
            2, _ => new WorkerResult(2, "queued"))));
        Assert.Equal(1, pool.PendingWorkCount);
        Assert.Equal(1, pool.ActiveWorkerCount);
        Assert.Equal(2, pool.InFlightWorkCount);

        Assert.False(pool.TrySchedule(Request(
            3, _ => new WorkerResult(3, "rejected"))));
        Assert.Equal(1, pool.PendingWorkCount);
        Assert.Equal(1, pool.ActiveWorkerCount);
        Assert.Equal(2, pool.InFlightWorkCount);
        Assert.Equal(1, pool.RejectedWorkCount);

        releaseWorker.Set();
        var completions = new List<ChunkGenerationDependencyCompletion>();
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                while (mailbox.TryDequeue(out var completion))
                    completions.Add(completion);
                return completions.Count == 2;
            },
            TimeSpan.FromSeconds(5)));
        Assert.Equal(1UL, completions[0].DependencyId);
        Assert.Equal(2UL, completions[1].DependencyId);
        Assert.Equal(0, pool.InFlightWorkCount);
    }

    [Fact]
    public async Task CompletionCapacityIsReservedBeforeWorkerAdmission()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(1);
        await using var pool = new ChunkGenerationWorkerPool(1, 4, mailbox);

        Assert.True(pool.TrySchedule(Request(
            1, _ => new WorkerResult(1, "complete"))));
        Assert.True(SpinWait.SpinUntil(
            () => pool.CompletedWorkCount == 1,
            TimeSpan.FromSeconds(5)));

        // The worker queue has room, but the undrained completion owns the only
        // mailbox slot. The second operation must remain with the coordinator.
        Assert.Equal(1, mailbox.PendingCount);
        Assert.False(pool.TrySchedule(Request(
            2, _ => new WorkerResult(2, "wait for mailbox"))));

        Assert.True(mailbox.TryDequeue(out var completion));
        Assert.Equal(1UL, completion.DependencyId);
        Assert.True(pool.TrySchedule(Request(
            2, _ => new WorkerResult(2, "retry"))));
    }

    [Fact]
    public async Task WorkerFailureIsPublishedAsCompletionInsteadOfEscapingPool()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(1);
        await using var pool = new ChunkGenerationWorkerPool(1, 1, mailbox);
        var error = new InvalidOperationException("slice failed");

        Assert.True(pool.TrySchedule(Request(
            1, _ => throw error)));
        ChunkGenerationDependencyCompletion completion = default;
        Assert.True(SpinWait.SpinUntil(
            () => mailbox.TryDequeue(out completion),
            TimeSpan.FromSeconds(5)));

        Assert.Same(error, completion.Error);
        Assert.Null(completion.Result);
        Assert.Equal(1, pool.FailedWorkCount);
        Assert.Equal(0, pool.ActiveWorkerCount);
    }

    [Fact]
    public async Task CancellationTokenIsPassedToWorkerOperation()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(1);
        await using var pool = new ChunkGenerationWorkerPool(1, 1, mailbox);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.True(pool.TrySchedule(Request(
            1,
            token =>
            {
                token.ThrowIfCancellationRequested();
                return new WorkerResult(1, "not reached");
            },
            cancellation.Token)));
        ChunkGenerationDependencyCompletion completion = default;
        Assert.True(SpinWait.SpinUntil(
            () => mailbox.TryDequeue(out completion),
            TimeSpan.FromSeconds(5)));

        Assert.IsType<OperationCanceledException>(completion.Error);
        Assert.Null(completion.Result);
        Assert.Equal(1, pool.FailedWorkCount);
    }

    [Fact]
    public async Task DisposeDrainsQueuedWorkAndRejectsNewRequests()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(2);
        var pool = new ChunkGenerationWorkerPool(1, 2, mailbox);
        Assert.True(pool.TrySchedule(Request(1, _ => new WorkerResult(1, "first"))));
        Assert.True(pool.TrySchedule(Request(2, _ => new WorkerResult(2, "second"))));

        await pool.DisposeAsync();

        Assert.Equal(2, mailbox.PendingCount);
        Assert.Equal(2, pool.CompletedWorkCount);
        Assert.Equal(0, pool.ActiveWorkerCount);
        Assert.Equal(0, pool.PendingWorkCount);
        Assert.Equal(0, pool.InFlightWorkCount);
        Assert.False(pool.TrySchedule(Request(3, _ => new WorkerResult(3, "late"))));
        Assert.Equal(1, pool.RejectedWorkCount);
    }

    private sealed record WorkerResult(int CellsProcessed, string Step);

    private static ChunkGenerationWorkerRequest Request(
        ulong dependencyId,
        Func<CancellationToken, object?> execute,
        CancellationToken cancellationToken = default) =>
        new(
            new ChunkGenerationTaskInstance(
                new ChunkGenerationTaskKey(
                    new ChunkColumnCoord(0, 0),
                    ChunkGenerationStepId.SectionTerrainLayers),
                Generation: 4),
            WaitToken: 3,
            DependencyId: dependencyId,
            Execute: execute,
            CancellationToken: cancellationToken);
}
