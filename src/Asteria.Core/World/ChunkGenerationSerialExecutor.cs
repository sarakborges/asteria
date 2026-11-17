namespace Asteria.Core.World;

/// <summary>
/// Per-dimension task-advance lane backed by the shared physical worker pool.
/// Independent task instances may advance concurrently, but a task instance
/// is never advanced twice before its previous result is collected. The
/// optional dependency lane remains serial for operations such as lighting.
/// </summary>
public sealed class ChunkGenerationSerialExecutor
{
    private readonly ChunkGenerationWorkerPool _workerPool;
    private readonly ChunkGenerationTaskAdvanceMailbox _completionMailbox = new();
    private readonly ChunkGenerationCompletionMailbox _dependencyCompletionMailbox = new();
    private readonly int _maximumConcurrentTaskAdvances;
    private readonly Dictionary<
        ChunkGenerationTaskInstance,
        WorldWorkerOperation<ChunkGenerationTaskAdvanceResult>>
        _activeTaskOperations = [];
    private ChunkGenerationWorkerRequest? _activeDependency;
    private ChunkGenerationCompletionMailbox? _activeDependencyMailbox;
    private WorldWorkerOperation<object?>? _activeDependencyOperation;

    public ChunkGenerationSerialExecutor(
        ChunkGenerationWorkerPool workerPool,
        int maximumConcurrentTaskAdvances = 1)
    {
        _workerPool = workerPool ??
            throw new ArgumentNullException(nameof(workerPool));
        if (maximumConcurrentTaskAdvances <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumConcurrentTaskAdvances));
        _maximumConcurrentTaskAdvances = maximumConcurrentTaskAdvances;
    }

    /// <summary>
    /// True while at least one task advance or serial dependency awaits
    /// collection by its owning coordinator.
    /// </summary>
    public bool HasInFlightTask =>
        _activeTaskOperations.Count > 0 || _activeDependency is not null;

    public int ActiveTaskCount => _activeTaskOperations.Count;

    public int AvailableTaskAdvanceSlots =>
        Math.Max(0, _maximumConcurrentTaskAdvances - _activeTaskOperations.Count);

    public ChunkGenerationTaskInstance? ActiveTask =>
        _activeTaskOperations.Count > 0
            ? _activeTaskOperations.Keys.First()
            : _activeDependency?.Task;

    /// <summary>
    /// Admits one bounded task advance when capacity remains. At most one
    /// advance per task instance may be in flight; independent tasks can use
    /// separate worker slots.
    /// </summary>
    public bool TrySchedule(
        ChunkGenerationTaskInstance task,
        Func<CancellationToken, ChunkGenerationTaskAdvanceResult> execute,
        CancellationToken cancellationToken,
        WorldWorkerPriority priority)
    {
        task.Validate();
        ArgumentNullException.ThrowIfNull(execute);
        if (_activeDependency is not null ||
            AvailableTaskAdvanceSlots == 0 ||
            _activeTaskOperations.ContainsKey(task))
            return false;

        if (!_workerPool.TryScheduleTaskAdvance(
                task,
                execute,
                cancellationToken,
                priority,
                _completionMailbox,
                out var operation))
            return false;

        _activeTaskOperations.Add(
            task,
            operation ?? throw new InvalidOperationException(
                "Task-advance admission returned no worker operation handle."));
        return true;
    }

    /// <summary>
    /// Admits one dependency fork on this serial lane. The callback executes on
    /// the shared physical pool, but the lane is not released until the owning
    /// dispatcher consumes its result. This is used for INITIALIZE_LIGHT and
    /// LIGHT dependencies, independently from the worldgen serial lane.
    /// </summary>
    public bool TryScheduleDependency(ChunkGenerationWorkerRequest request) =>
        TryScheduleDependency(request, _dependencyCompletionMailbox);

    /// <summary>
    /// Admits a dependency whose result is consumed by another owner while
    /// preserving the single serial lane. The mailbox identity is retained so
    /// the wrong coordinator cannot consume and release another owner's work.
    /// </summary>
    public bool TryScheduleDependency(
        ChunkGenerationWorkerRequest request,
        ChunkGenerationCompletionMailbox completionMailbox)
    {
        request.Validate();
        ArgumentNullException.ThrowIfNull(completionMailbox);
        if (_activeTaskOperations.Count > 0 || _activeDependency is not null)
            return false;

        if (!_workerPool.TrySchedule(
                request,
                completionMailbox,
                out var operation))
            return false;

        _activeDependency = request;
        _activeDependencyMailbox = completionMailbox;
        _activeDependencyOperation = operation ??
            throw new InvalidOperationException(
                "Serial dependency admission returned no worker operation handle.");
        return true;
    }

    /// <summary>
    /// Collects one dependency result and releases this lane. The dispatcher
    /// still owns task-lifecycle and wait-token transitions.
    /// </summary>
    public bool TryCollectDependency(
        out ChunkGenerationDependencyCompletion completion) =>
        TryCollectDependency(_dependencyCompletionMailbox, out completion);

    public bool TryCollectDependency(
        ChunkGenerationCompletionMailbox completionMailbox,
        out ChunkGenerationDependencyCompletion completion)
    {
        ArgumentNullException.ThrowIfNull(completionMailbox);
        if (!ReferenceEquals(_activeDependencyMailbox, completionMailbox) ||
            !completionMailbox.TryDequeue(out completion))
        {
            completion = default;
            return false;
        }

        if (_activeDependency is not { } active ||
            active.Task != completion.Task ||
            active.WaitToken != completion.WaitToken ||
            active.DependencyId != completion.DependencyId ||
            _activeDependencyOperation is null)
        {
            throw new InvalidOperationException(
                $"Serial dependency completion does not match its active request: {completion.Task}");
        }

        _activeDependency = null;
        _activeDependencyMailbox = null;
        _activeDependencyOperation = null;
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

        if (!_activeTaskOperations.Remove(completion.Task, out _))
        {
            throw new InvalidOperationException(
                $"Task-advance completion does not match an active task: {completion.Task}");
        }

        return true;
    }

    /// <summary>Reprioritizes the active continuation while it is still queued.</summary>
    public bool TryUpdatePriority(
        ChunkGenerationTaskInstance task,
        WorldWorkerPriority priority)
    {
        if (_activeTaskOperations.TryGetValue(task, out var operation))
            return _workerPool.TryUpdatePriority(operation, priority);

        if (_activeDependency is { } dependency &&
            dependency.Task == task &&
            _activeDependencyOperation is not null)
            return _workerPool.TryUpdatePriority(
                _activeDependencyOperation, priority);

        return false;
    }
}
