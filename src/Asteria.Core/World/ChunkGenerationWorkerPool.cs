namespace Asteria.Core.World;

/// <summary>
/// One immutable CPU operation forked by a generation step. Execute must work
/// only with detached inputs and return a detached result payload. The shared
/// executor never reads or mutates live VoxelWorld or dispatcher state.
/// </summary>
public readonly record struct ChunkGenerationWorkerRequest(
    ChunkGenerationTaskInstance Task,
    ulong WaitToken,
    ulong DependencyId,
    Func<CancellationToken, object?> Execute,
    CancellationToken CancellationToken = default)
{
    public void Validate()
    {
        Task.Validate();
        if (WaitToken == 0)
            throw new ArgumentOutOfRangeException(
                nameof(WaitToken), "Wait token zero is reserved.");
        if (DependencyId == 0)
            throw new ArgumentOutOfRangeException(
                nameof(DependencyId), "Dependency ID zero is reserved.");
        ArgumentNullException.ThrowIfNull(Execute);
    }
}

/// <summary>
/// Generation-specific adapter over the session's shared WorldWorkerPool.
/// It reserves mailbox capacity before admission and publishes completions
/// from worker threads without touching coordinator-owned dispatcher state.
/// </summary>
public sealed class ChunkGenerationWorkerPool : IAsyncDisposable
{
    private readonly WorldWorkerPool _workerPool;
    private readonly ChunkGenerationCompletionMailbox _completionMailbox;
    private readonly bool _ownsWorkerPool;
    private readonly TaskCompletionSource _drained =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;
    private int _pendingWorkCount;
    private int _activeWorkerCount;
    private int _inFlightWorkCount;
    private long _completedWorkCount;
    private long _failedWorkCount;
    private long _rejectedWorkCount;

    /// <summary>
    /// Creates an isolated executor for tests and standalone benchmarks. The
    /// running game injects its one shared world-worker pool instead.
    /// </summary>
    public ChunkGenerationWorkerPool(
        int workerCount,
        int pendingCapacity,
        ChunkGenerationCompletionMailbox completionMailbox)
        : this(
            new WorldWorkerPool(workerCount, pendingCapacity),
            completionMailbox,
            ownsWorkerPool: true)
    {
    }

    public ChunkGenerationWorkerPool(
        WorldWorkerPool workerPool,
        ChunkGenerationCompletionMailbox completionMailbox)
        : this(workerPool, completionMailbox, ownsWorkerPool: false)
    {
    }

    private ChunkGenerationWorkerPool(
        WorldWorkerPool workerPool,
        ChunkGenerationCompletionMailbox completionMailbox,
        bool ownsWorkerPool)
    {
        _workerPool = workerPool ??
            throw new ArgumentNullException(nameof(workerPool));
        _completionMailbox = completionMailbox ??
            throw new ArgumentNullException(nameof(completionMailbox));
        _ownsWorkerPool = ownsWorkerPool;
    }

    public int WorkerCount => _workerPool.WorkerCount;

    public int PendingCapacity => _workerPool.PendingCapacity;

    public int AvailableSubmissionCount => _workerPool.AvailableSubmissionCount;

    public int PendingWorkCount => Volatile.Read(ref _pendingWorkCount);

    public int ActiveWorkerCount => Volatile.Read(ref _activeWorkerCount);

    public int InFlightWorkCount => Volatile.Read(ref _inFlightWorkCount);

    public long CompletedWorkCount => Interlocked.Read(ref _completedWorkCount);

    public long FailedWorkCount => Interlocked.Read(ref _failedWorkCount);

    public long RejectedWorkCount => Interlocked.Read(ref _rejectedWorkCount);

    /// <summary>
    /// Queues a CPU operation without blocking. False means either the shared
    /// worker queue or completion mailbox has no capacity; the coordinator
    /// retains the request and retries after capacity becomes available.
    /// </summary>
    public bool TrySchedule(ChunkGenerationWorkerRequest request)
    {
        request.Validate();
        if (Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Increment(ref _rejectedWorkCount);
            return false;
        }

        if (!_completionMailbox.TryReserve(out var reservation))
        {
            Interlocked.Increment(ref _rejectedWorkCount);
            return false;
        }

        // Publish the lane counters before submitting. A worker may dequeue
        // and complete this operation before TrySchedule returns.
        Interlocked.Increment(ref _inFlightWorkCount);
        Interlocked.Increment(ref _pendingWorkCount);
        if (_workerPool.TrySchedule(
                request.Execute,
                request.CancellationToken,
                out _,
                onStarted: () =>
                {
                    Interlocked.Decrement(ref _pendingWorkCount);
                    Interlocked.Increment(ref _activeWorkerCount);
                },
                onCompleted: result =>
                {
                    try
                    {
                        var completion = new ChunkGenerationDependencyCompletion(
                            request.Task,
                            request.WaitToken,
                            request.DependencyId,
                            Result: result.Result,
                            Error: result.Error);
                        if (!reservation!.TryPublish(completion))
                        {
                            Interlocked.Increment(ref _failedWorkCount);
                            return;
                        }

                        if (result.Error is null)
                            Interlocked.Increment(ref _completedWorkCount);
                        else
                            Interlocked.Increment(ref _failedWorkCount);
                    }
                    finally
                    {
                        reservation!.Dispose();
                        Interlocked.Decrement(ref _activeWorkerCount);
                        Interlocked.Decrement(ref _inFlightWorkCount);
                        CompleteDrainIfNeeded();
                    }
                }))
        {
            return true;
        }

        Interlocked.Decrement(ref _pendingWorkCount);
        Interlocked.Decrement(ref _inFlightWorkCount);
        reservation!.Dispose();
        Interlocked.Increment(ref _rejectedWorkCount);
        CompleteDrainIfNeeded();
        return false;
    }

    /// <summary>
    /// Stops generation-specific admission. An injected shared executor remains
    /// alive for other runtimes; an isolated test executor is drained and closed.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            CompleteDrainIfNeeded();

        return _ownsWorkerPool
            ? _workerPool.DisposeAsync()
            : new ValueTask(_drained.Task);
    }

    private void CompleteDrainIfNeeded()
    {
        if (Volatile.Read(ref _disposed) != 0 &&
            Volatile.Read(ref _inFlightWorkCount) == 0)
            _drained.TrySetResult();
    }
}
