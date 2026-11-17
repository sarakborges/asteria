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
/// It accounts for both dependency forks and serial task-advance operations.
/// The dispatcher owns logical backlog; the shared pool bounds physical work.
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

    public int AvailableWorkerSlots =>
        Math.Max(0, _workerPool.WorkerCount - _workerPool.InFlightWorkCount);

    public int PendingWorkCount => Volatile.Read(ref _pendingWorkCount);

    public int ActiveWorkerCount => Volatile.Read(ref _activeWorkerCount);

    public int InFlightWorkCount => Volatile.Read(ref _inFlightWorkCount);

    public long CompletedWorkCount => Interlocked.Read(ref _completedWorkCount);

    public long FailedWorkCount => Interlocked.Read(ref _failedWorkCount);

    public long RejectedWorkCount => Interlocked.Read(ref _rejectedWorkCount);

    /// <summary>
    /// Submits detached work for one generation dependency. False means the
    /// adapter or executor is retiring; queued demand is not worker-count capped.
    /// </summary>
    public bool TrySchedule(ChunkGenerationWorkerRequest request) =>
        TrySchedule(request, out _);

    public bool TrySchedule(
        ChunkGenerationWorkerRequest request,
        out WorldWorkerOperation<object?>? operation) =>
        TrySchedule(request, _completionMailbox, out operation);

    /// <summary>
    /// Schedules a dependency fork to a lane-specific completion mailbox while
    /// retaining the same shared physical worker pool and admission accounting.
    /// Used by the separate serial light executor.
    /// </summary>
    internal bool TrySchedule(
        ChunkGenerationWorkerRequest request,
        ChunkGenerationCompletionMailbox completionMailbox,
        out WorldWorkerOperation<object?>? operation)
    {
        request.Validate();
        ArgumentNullException.ThrowIfNull(completionMailbox);
        var priority = request.Priority ??
            WorldWorkerPriority.FromPriorityClass(
                WorldWorkerPool.DefaultPriority);

        return TryScheduleCore(
            request.Execute,
            request.CancellationToken,
            priority,
            result => completionMailbox.Publish(
                new ChunkGenerationDependencyCompletion(
                    request.Task,
                    request.WaitToken,
                    request.DependencyId,
                    Result: result.Result,
                    Error: result.Error)),
            out operation);
    }

    /// <summary>
    /// Submits one logical task advance. The dispatcher guarantees at most
    /// one advance per task instance; independent tasks may share the worker
    /// pool and publish to the multi-producer completion mailbox.
    /// </summary>
    internal bool TryScheduleTaskAdvance(
        ChunkGenerationTaskInstance task,
        Func<CancellationToken, ChunkGenerationTaskAdvanceResult> execute,
        CancellationToken cancellationToken,
        WorldWorkerPriority priority,
        ChunkGenerationTaskAdvanceMailbox completionMailbox,
        out WorldWorkerOperation<ChunkGenerationTaskAdvanceResult>? operation)
    {
        task.Validate();
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(completionMailbox);

        return TryScheduleCore(
            execute,
            cancellationToken,
            priority,
            result => completionMailbox.Publish(
                new ChunkGenerationTaskAdvanceCompletion(
                    task,
                    result.Result,
                    result.Error)),
            out operation);
    }

    /// <summary>
    /// Changes the priority of any queued work owned by this adapter, including
    /// task advances and dependency forks.
    /// </summary>
    public bool TryUpdatePriority<TResult>(
        WorldWorkerOperation<TResult> operation,
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

    private bool TryScheduleCore<TResult>(
        Func<CancellationToken, TResult> execute,
        CancellationToken cancellationToken,
        WorldWorkerPriority priority,
        Action<WorldWorkerCompletion<TResult>> publishCompletion,
        out WorldWorkerOperation<TResult>? operation)
    {
        ArgumentNullException.ThrowIfNull(execute);
        ArgumentNullException.ThrowIfNull(publishCompletion);
        operation = null;

        if (Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Increment(ref _rejectedWorkCount);
            return false;
        }

        Interlocked.Increment(ref _inFlightWorkCount);
        Interlocked.Increment(ref _pendingWorkCount);
        if (_workerPool.TrySchedule(
                execute,
                cancellationToken,
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
                        publishCompletion(result);
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
                }))
        {
            return true;
        }

        Interlocked.Decrement(ref _pendingWorkCount);
        Interlocked.Decrement(ref _inFlightWorkCount);
        Interlocked.Increment(ref _rejectedWorkCount);
        CompleteDrainIfNeeded();
        return false;
    }

    private void CompleteDrainIfNeeded()
    {
        if (Volatile.Read(ref _disposed) != 0 &&
            Volatile.Read(ref _inFlightWorkCount) == 0)
            _drained.TrySetResult();
    }
}
