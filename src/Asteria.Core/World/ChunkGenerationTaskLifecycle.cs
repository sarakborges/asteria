namespace Asteria.Core.World;

/// <summary>Coordinator-owned lifecycle states for one logical column generation task.</summary>
public enum ChunkGenerationTaskState : byte
{
    ReadyToRun,
    Running,
    WaitingForDependencies,
    ReadyToResume,
    Completed,
    Cancelled,
    Failed,
}

/// <summary>Result of delivering one dependency completion to a waiting task.</summary>
public enum ChunkGenerationDependencySignalResult : byte
{
    /// <summary>The wait token belongs to an earlier or already-closed wait.</summary>
    StaleWait,
    /// <summary>The dependency ID was not part of this wait, or was already signalled.</summary>
    DuplicateOrUnknownDependency,
    /// <summary>The dependency was accepted; at least one dependency remains.</summary>
    StillWaiting,
    /// <summary>All dependencies completed and the task is ready to resume.</summary>
    ReadyToResume,
}

/// <summary>
/// Serial-coordinator-owned state for a cooperative generation task.
/// Worker continuations must post completion messages; only the serial
/// coordinator may call these transition methods.
/// </summary>
public sealed class ChunkGenerationTaskLifecycle
{
    private readonly HashSet<ulong> _pendingDependencyIds = [];
    private ulong _nextWaitToken;
    private ulong _activeWaitToken;

    public ChunkGenerationTaskState State { get; private set; } =
        ChunkGenerationTaskState.ReadyToRun;

    public ulong? CurrentWaitToken =>
        _activeWaitToken == 0 ? null : _activeWaitToken;

    public int PendingDependencyCount => _pendingDependencyIds.Count;

    public Exception? Failure { get; private set; }

    /// <summary>
    /// Begins one execution slice. A task may start initially or resume after
    /// every dependency from its previous wait has completed.
    /// </summary>
    public bool TryBeginExecution()
    {
        if (State is not (
            ChunkGenerationTaskState.ReadyToRun or
            ChunkGenerationTaskState.ReadyToResume))
            return false;

        State = ChunkGenerationTaskState.Running;
        return true;
    }

    /// <summary>
    /// Suspends a running task on a non-empty set of unique dependency IDs.
    /// The returned wait token distinguishes this wait from all later waits
    /// made by the same task.
    /// </summary>
    public ulong BeginWaitingForDependencies(
        IEnumerable<ulong> dependencyIds)
    {
        ArgumentNullException.ThrowIfNull(dependencyIds);
        if (State != ChunkGenerationTaskState.Running)
            throw new InvalidOperationException(
                $"A generation task can only wait while running; current state is {State}.");

        var pending = new HashSet<ulong>();
        foreach (var dependencyId in dependencyIds)
        {
            if (dependencyId == 0)
                throw new ArgumentOutOfRangeException(
                    nameof(dependencyIds),
                    "Dependency ID zero is reserved.");
            if (!pending.Add(dependencyId))
                throw new ArgumentException(
                    $"Dependency ID {dependencyId} was registered more than once.",
                    nameof(dependencyIds));
        }

        if (pending.Count == 0)
            throw new ArgumentException(
                "A task cannot suspend without at least one pending dependency.",
                nameof(dependencyIds));
        if (_nextWaitToken == ulong.MaxValue)
            throw new InvalidOperationException(
                "Generation task wait token space was exhausted.");

        _nextWaitToken++;
        _activeWaitToken = _nextWaitToken;
        _pendingDependencyIds.Clear();
        _pendingDependencyIds.UnionWith(pending);
        State = ChunkGenerationTaskState.WaitingForDependencies;
        return _activeWaitToken;
    }

    /// <summary>
    /// Applies one worker completion after it has been delivered to the serial
    /// coordinator. Duplicate dependency IDs and stale wait tokens are ignored.
    /// </summary>
    public ChunkGenerationDependencySignalResult SignalDependencyCompleted(
        ulong waitToken,
        ulong dependencyId)
    {
        if (State != ChunkGenerationTaskState.WaitingForDependencies ||
            waitToken == 0 ||
            waitToken != _activeWaitToken)
            return ChunkGenerationDependencySignalResult.StaleWait;

        if (dependencyId == 0 || !_pendingDependencyIds.Remove(dependencyId))
            return ChunkGenerationDependencySignalResult.DuplicateOrUnknownDependency;

        if (_pendingDependencyIds.Count > 0)
            return ChunkGenerationDependencySignalResult.StillWaiting;

        _activeWaitToken = 0;
        State = ChunkGenerationTaskState.ReadyToResume;
        return ChunkGenerationDependencySignalResult.ReadyToResume;
    }

    /// <summary>Marks a running task complete exactly once.</summary>
    public bool TryComplete()
    {
        if (State != ChunkGenerationTaskState.Running)
            return false;

        State = ChunkGenerationTaskState.Completed;
        ClearWaitState();
        return true;
    }

    /// <summary>
    /// Cancels any non-terminal task and invalidates all outstanding wait tokens.
    /// </summary>
    public bool TryCancel()
    {
        if (IsTerminal(State))
            return false;

        State = ChunkGenerationTaskState.Cancelled;
        ClearWaitState();
        return true;
    }

    /// <summary>
    /// Fails any non-terminal task and retains the cause for dispatcher reporting.
    /// </summary>
    public bool TryFail(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (IsTerminal(State))
            return false;

        Failure = error;
        State = ChunkGenerationTaskState.Failed;
        ClearWaitState();
        return true;
    }

    private void ClearWaitState()
    {
        _activeWaitToken = 0;
        _pendingDependencyIds.Clear();
    }

    private static bool IsTerminal(ChunkGenerationTaskState state) =>
        state is ChunkGenerationTaskState.Completed or
            ChunkGenerationTaskState.Cancelled or
            ChunkGenerationTaskState.Failed;
}
