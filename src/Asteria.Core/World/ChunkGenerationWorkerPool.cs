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
    CancellationToken CancellationToken = default,
    WorldWorkerPriority? Priority = null)
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
/// It publishes detached completion messages to an unbounded mailbox; worker
/// count limits execution, while the dispatcher owns logical task backlog.
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
        ChunkGenerationCompletionMailbox completionMailbox)
        : this(
            new WorldWorkerPool(workerCount),
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

    internal WorldWorkerPool WorkerPool => _workerPool;

    public int WorkerCount => _workerPool.WorkerCount;

    public int PendingWorkCount => Volatile.Read(ref _pendingWorkCount);

    public int ActiveWorkerCount => Volatile.Read(ref _activeWorkerCount);

    public int InFlightWorkCount => Volatile.Read(ref _inFlightWorkCount);

    public long CompletedWorkCount => Interlocked.Read(ref _completedWorkCount);

    public long FailedWorkCount => Interlocked.Read(ref _failedWorkCount);

    public long RejectedWorkCount => Interlocked.Read(ref _rejectedWorkCount);

    /// <summary>
    /// Submits work to the shared priority queue. False means the adapter or
    /// executor is retiring; pending queue length is not a worker admission cap.
    /// </summary>
    public bool TrySchedule(ChunkGenerationWorkerRequest request) =>
        TrySchedule(request, out _);

    public bool TrySchedule(
        ChunkGenerationWorkerRequest request,
        out WorldWorkerOperation<object?>? operation)
    {
        request.Validate();
        operation = null;
        if (Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Increment(ref _rejectedWorkCount);
            return false;
        }

        Interlocked.Increment(ref _inFlightWorkCount);
        Interlocked.Increment(ref _pendingWorkCount);
        var priority = request.Priority ??
            WorldWorkerPriority.FromPriorityClass(
                WorldWorkerPool.DefaultPriority);
        if (_workerPool.TrySchedule(
                request.Execute,
                request.CancellationToken,
                out operation,
                priority,
                onStarted: () =>
                {
                    Interlocked.Decrement(ref _pendingWorkCount);
                    Interlocked.Increment(ref _activeWorkerCount);
                },
                onCompleted: result =>
                {
                    try
                    {
                        _completionMailbox.Publish(
                            new ChunkGenerationDependencyCompletion(
                                request.Task,
                                request.WaitToken,
                                request.DependencyId,
                                Result: result.Result,
                                Error: result.Error));

                        if (result.Error is null)
                            Interlocked.Increment(ref _completedWorkCount);
                        else
                            Interlocked.Increment(ref _failedWorkCount);
                    }
                    finally
                    {
                        Interlocked.Decrement(ref _activeWorkerCount);
                        Interlocked.Decrement(ref _inFlightWorkCount);
                        CompleteDrainIfNeeded();
                    }
                },
                priority: request.Priority))
        {
            return true;
        }

        Interlocked.Decrement(ref _pendingWorkCount);
        Interlocked.Decrement(ref _inFlightWorkCount);
        Interlocked.Increment(ref _rejectedWorkCount);
        CompleteDrainIfNeeded();
        return false;
    }

    public bool TryUpdatePriority(
        WorldWorkerOperation<object?> operation,
        WorldWorkerPriority priority) =>
        _workerPool.TryUpdatePriority(operation, priority);

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
