using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationWorkerPoolTests
{
    [Fact]
    public void ConstructorRequiresPositiveWorkerCount()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChunkGenerationWorkerPool(0, mailbox));
    }

    [Fact]
    public async Task WorkerPublishesDetachedResultToCompletionMailbox()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        var result = new WorkerResult(64, "terrain-layer");

        Assert.True(pool.TrySchedule(Request(1, _ => result)));
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
    public async Task LargeGenerationBacklogQueuesWithoutIncreasingPhysicalConcurrency()
    {
        const int queuedOperations = 128;
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        using var releaseWorker = new ManualResetEventSlim(false);
        using var workerStarted = new ManualResetEventSlim(false);

        Assert.True(pool.TrySchedule(Request(1, _ =>
        {
            workerStarted.Set();
            releaseWorker.Wait();
            return new WorkerResult(1, "blocking");
        })));
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        for (var index = 0; index < queuedOperations; index++)
        {
            var dependencyId = (ulong)(index + 2);
            Assert.True(pool.TrySchedule(Request(
                dependencyId,
                _ => new WorkerResult((int)dependencyId, "queued"))));
        }

        Assert.Equal(queuedOperations, pool.PendingWorkCount);
        Assert.Equal(1, pool.ActiveWorkerCount);
        Assert.Equal(queuedOperations + 1, pool.InFlightWorkCount);
        Assert.Equal(0, pool.RejectedWorkCount);

        releaseWorker.Set();
        var completions = new List<ChunkGenerationDependencyCompletion>();
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                while (mailbox.TryDequeue(out var completion))
                    completions.Add(completion);
                return completions.Count == queuedOperations + 1;
            },
            TimeSpan.FromSeconds(10)));

        Assert.Equal(queuedOperations + 1, completions.Count);
        Assert.Equal(
            Enumerable.Range(1, queuedOperations + 1)
                .Select(value => (ulong)value),
            completions.Select(completion => completion.DependencyId).Order());
        Assert.Equal(queuedOperations + 1, pool.CompletedWorkCount);
        Assert.Equal(0, pool.InFlightWorkCount);
    }

    [Fact]
    public async Task UndrainedMailboxDoesNotRejectFurtherCompletedWork()
    {
        const int operations = 64;
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(2, mailbox);

        for (var index = 1; index <= operations; index++)
        {
            var dependencyId = (ulong)index;
            Assert.True(pool.TrySchedule(Request(
                dependencyId,
                _ => new WorkerResult(index, "completion"))));
        }

        Assert.True(SpinWait.SpinUntil(
            () => pool.CompletedWorkCount == operations,
            TimeSpan.FromSeconds(5)));
        Assert.Equal(operations, mailbox.PendingCount);
        Assert.Equal(0, pool.RejectedWorkCount);

        var drained = 0;
        while (mailbox.TryDequeue(out _))
            drained++;
        Assert.Equal(operations, drained);
        Assert.Equal(0, mailbox.PendingCount);
    }

    [Fact]
    public async Task WorkerFailureIsPublishedAsCompletionInsteadOfEscapingPool()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        var error = new InvalidOperationException("slice failed");

        Assert.True(pool.TrySchedule(Request(1, _ => throw error)));
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
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
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
        var mailbox = new ChunkGenerationCompletionMailbox();
        var pool = new ChunkGenerationWorkerPool(1, mailbox);
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
