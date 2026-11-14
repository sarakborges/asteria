namespace Asteria.Core.World;

/// <summary>
/// Per-dimension serial continuation lane backed by the shared physical worker
/// pool. It admits one task advance at a time; consecutive advances may execute
/// on different CLR ThreadPool workers. Dependency forks are scheduled through
/// the same pool by the owning ChunkGenerationDispatcher.
/// </summary>
public sealed class ChunkGenerationSerialExecutor
{
    private readonly ChunkGenerationWorkerPool _workerPool;
    private readonly ChunkGenerationTaskAdvanceMailbox _completionMailbox = new();
    private ChunkGenerationTaskInstance? _activeTask;
    private WorldWorkerOperation<ChunkGenerationTaskAdvanceResult>? _activeOperation;

    public ChunkGenerationSerialExecutor(ChunkGenerationWorkerPool workerPool)
    {
        _workerPool = workerPool ??
            throw new ArgumentNullException(nameof(workerPool));
    }

    /// <summary>
    /// True from admission until the owning dispatcher consumes the completion.
    /// A published completion does not free the lane until its task transition
    /// has been applied by the authoritative dispatcher.
    /// </summary>
    public bool HasInFlightTask => _activeTask is not null;

    public ChunkGenerationTaskInstance? ActiveTask => _activeTask;

    /// <summary>
    /// Admits one bounded task advance if this dimension's serial lane is free.
    /// The callback runs on the shared worker pool, not on the world/main thread.
    /// </summary>
    public bool TrySchedule(
        ChunkGenerationTaskInstance task,
        Func<CancellationToken, ChunkGenerationTaskAdvanceResult> execute,
        CancellationToken cancellationToken,
        WorldWorkerPriority priority)
    {
        task.Validate();
        ArgumentNullException.ThrowIfNull(execute);
        if (_activeTask is not null)
            return false;

        if (!_workerPool.TryScheduleTaskAdvance(
                task,
                execute,
                cancellationToken,
                priority,
                _completionMailbox,
                out var operation))
            return false;

        _activeTask = task;
        _activeOperation = operation ??
            throw new InvalidOperationException(
                "Serial executor admission returned no worker operation handle.");
        return true;
    }

    /// <summary>
    /// Collects the one completed advance and releases the serial slot. Only
    /// the owning dispatcher may consume the result and transition task state.
    /// </summary>
    public bool TryCollect(
        out ChunkGenerationTaskAdvanceCompletion completion)
    {
        if (!_completionMailbox.TryDequeue(out completion))
            return false;

        if (_activeTask is not { } activeTask ||
            activeTask != completion.Task ||
            _activeOperation is null)
        {
            throw new InvalidOperationException(
                $"Serial executor completion does not match its active task: {completion.Task}");
        }

        _activeTask = null;
        _activeOperation = null;
        return true;
    }

    /// <summary>Reprioritizes the active continuation while it is still queued.</summary>
    public bool TryUpdatePriority(
        ChunkGenerationTaskInstance task,
        WorldWorkerPriority priority)
    {
        if (_activeTask != task || _activeOperation is null)
            return false;

        return _workerPool.TryUpdatePriority(_activeOperation, priority);
    }
}
