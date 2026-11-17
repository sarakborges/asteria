namespace Asteria.Core.World;

/// <summary>
/// Result delivered by a holder's status-specific generation future.
/// </summary>
public readonly record struct ChunkGenerationFutureResult(
    bool Succeeded,
    ChunkGenerationStatus Status,
    string? FailureReason,
    ulong Revision = 0,
    Chunk? Product = null)
{
    public static ChunkGenerationFutureResult Success(
        ChunkGenerationStatus status,
        ulong revision = 0,
        Chunk? product = null) =>
        new(true, status, null, revision, product);

    public static ChunkGenerationFutureResult Unloaded(
        ChunkGenerationStatus status,
        string reason,
        ulong revision = 0) =>
        new(false, status, reason, revision);
}

/// <summary>
/// Section-local generation progress owned by its horizontal holder. Each
/// status future is distinct for the exact three-dimensional ChunkCoord.
/// </summary>
internal sealed class ChunkSectionGenerationProgressState
{
    private readonly Dictionary<ChunkGenerationStatus,
        TaskCompletionSource<ChunkGenerationFutureResult>> _futures =
        CreateGenerationFutures();
    private Chunk? _completedProduct;

    public ChunkGenerationStatus CompletedStatus { get; private set; }

    public ulong Revision { get; private set; } = 1;

    public bool HasProgressOrCompletedFuture =>
        CompletedStatus != ChunkGenerationStatus.Unloaded ||
        _futures.Values.Any(future => future.Task.IsCompleted);

    public Task<ChunkGenerationFutureResult> GetFuture(
        ChunkGenerationStatus status,
        ChunkGenerationStatus highestAllowedStatus,
        int ticketLevel)
    {
        ValidateStatus(status);
        if (status > highestAllowedStatus)
        {
            return Task.FromResult(ChunkGenerationFutureResult.Unloaded(
                status,
                $"Ticket level {ticketLevel} does not permit {status} for this section.",
                Revision));
        }

        return _futures[status].Task;
    }

    public bool Complete(
        ChunkGenerationStatus completedStatus,
        ChunkGenerationStatus highestAllowedStatus,
        int ticketLevel,
        Chunk? product)
    {
        ValidateStatus(completedStatus);
        if (completedStatus > CompletedStatus)
            CompletedStatus = completedStatus;
        if (product is not null)
            _completedProduct = product;
        Reconcile(highestAllowedStatus, ticketLevel);
        return completedStatus <= highestAllowedStatus &&
            _futures.Any(entry =>
                entry.Key <= completedStatus &&
                entry.Value.Task.IsCompletedSuccessfully &&
                entry.Value.Task.Result.Succeeded &&
                entry.Value.Task.Result.Revision == Revision);
    }

    public void Reconcile(
        ChunkGenerationStatus highestAllowedStatus,
        int ticketLevel)
    {
        foreach (var status in _futures.Keys.ToArray())
        {
            var future = _futures[status];
            if (status > highestAllowedStatus)
            {
                if (future.Task.IsCompletedSuccessfully &&
                    future.Task.Result.Succeeded)
                {
                    var demoted = NewGenerationFuture();
                    demoted.TrySetResult(ChunkGenerationFutureResult.Unloaded(
                        status,
                        $"Ticket level {ticketLevel} no longer permits {status} for this section.",
                        Revision));
                    _futures[status] = demoted;
                }
                else
                {
                    future.TrySetResult(ChunkGenerationFutureResult.Unloaded(
                        status,
                        $"Ticket level {ticketLevel} does not permit {status} for this section.",
                        Revision));
                }
                continue;
            }

            if (status <= CompletedStatus)
            {
                if (future.Task.IsCompletedSuccessfully &&
                    !future.Task.Result.Succeeded)
                {
                    future = NewGenerationFuture();
                    _futures[status] = future;
                }
                future.TrySetResult(ChunkGenerationFutureResult.Success(
                    status, Revision,
                    status >= ChunkGenerationStatus.Light
                        ? _completedProduct
                        : null));
            }
            else if (future.Task.IsCompletedSuccessfully &&
                     !future.Task.Result.Succeeded)
            {
                _futures[status] = NewGenerationFuture();
            }
        }
    }

    public void Reset(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (Revision == ulong.MaxValue)
            throw new InvalidOperationException(
                "Section generation revision space was exhausted.");

        var failedRevision = Revision;
        foreach (var (status, future) in _futures)
            future.TrySetResult(ChunkGenerationFutureResult.Unloaded(
                status, reason, failedRevision));
        Revision++;
        CompletedStatus = ChunkGenerationStatus.Unloaded;
        _completedProduct = null;
        foreach (var status in _futures.Keys.ToArray())
            _futures[status] = NewGenerationFuture();
    }

    private static void ValidateStatus(ChunkGenerationStatus status)
    {
        if (!Enum.IsDefined(status) || status == ChunkGenerationStatus.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(status));
    }

    private static Dictionary<ChunkGenerationStatus,
        TaskCompletionSource<ChunkGenerationFutureResult>> CreateGenerationFutures() =>
        Enum.GetValues<ChunkGenerationStatus>()
            .Where(status => status != ChunkGenerationStatus.Unloaded)
            .ToDictionary(status => status, _ => NewGenerationFuture());

    private static TaskCompletionSource<ChunkGenerationFutureResult>
        NewGenerationFuture() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}


/// <summary>
/// Generation-side state for one horizontal chunk column. It owns the
/// ticket-derived generation ceiling and per-section generation futures;
/// gameplay full-status promotion is owned by <see cref="ChunkHolder"/>.
/// </summary>
public abstract class GenerationChunkHolder
{
    // Minecraft 26.3 uses an AtomicReferenceArray indexed by ChunkStatus.
    // The holder gate supplies the same atomic get-or-create semantics while
    // also coordinating ticket ceilings, task ownership and section progress.
    private readonly object _stateGate = new();
    private readonly TaskCompletionSource<ChunkGenerationFutureResult>?[]
        _generationFutures =
            new TaskCompletionSource<ChunkGenerationFutureResult>?[
                Enum.GetValues<ChunkGenerationStatus>().Length];
    private readonly Dictionary<ChunkCoord, ChunkSectionGenerationProgressState>
        _sectionGenerationProgress = [];
    private ChunkGenerationTaskInstance? _generationTask;
    private ChunkGenerationStatus _generationTaskTargetStatus =
        ChunkGenerationStatus.Unloaded;
    private ulong _generationProgressRevision = 1;

    protected GenerationChunkHolder(
        ChunkColumnCoord position,
        int ticketLevel)
    {
        ValidateTicketLevel(ticketLevel);
        Position = position;
        TicketLevel = ticketLevel;
        QueuePriority = new ChunkGenerationTaskPriority(
            ticketLevel,
            long.MaxValue,
            long.MaxValue,
            0,
            position.Z,
            position.X);
        HighestAllowedGenerationStatus =
            ChunkLevel.GetGenerationStatus(ticketLevel);
        TicketLevelRevision = 1;
        ReconcileGenerationFutures();
    }

    public ChunkColumnCoord Position { get; }

    public int TicketLevel { get; private set; }

    /// <summary>
    /// Holder-owned effective scheduling key shared by worldgen and lighting
    /// dispatch. It includes ticket urgency and deterministic spatial ordering;
    /// the initial max-distance sentinel is replaced when residency admits work.
    /// </summary>
    public ChunkGenerationTaskPriority QueuePriority { get; private set; }

    public bool HasQueuePriorityContext { get; private set; }

    public ulong QueuePriorityRevision { get; private set; } = 1;

    public ChunkGenerationStatus HighestAllowedGenerationStatus { get; private set; }

    /// <summary>
    /// The one active column-generation task owned by this holder, matching
    /// Minecraft 26.3 GenerationChunkHolder.task. Auxiliary lighting and
    /// reconciliation operations are not generation tasks owned here.
    /// </summary>
    public ChunkGenerationTaskInstance? GenerationTask
    {
        get
        {
            lock (_stateGate)
                return _generationTask;
        }
    }

    /// <summary>Immutable target captured when the active generation task was admitted.</summary>
    public ChunkGenerationStatus? GenerationTaskTargetStatus
    {
        get
        {
            lock (_stateGate)
                return _generationTask is null ? null : _generationTaskTargetStatus;
        }
    }

    public ChunkGenerationStatus CompletedGenerationStatus { get; private set; }

    /// <summary>
    /// Highest incomplete generation status explicitly requested from this
    /// holder and still permitted by its current ticket ceiling.
    /// Unrequested statuses never create producer demand.
    /// </summary>
    public ChunkGenerationStatus? HighestPendingGenerationStatus
    {
        get
        {
            lock (_stateGate)
            {
                for (var index = _generationFutures.Length - 1; index > 0; index--)
                {
                    var future = _generationFutures[index];
                    if (future is null ||
                        index > (int)HighestAllowedGenerationStatus ||
                        future.Task.IsCompleted)
                        continue;

                    return (ChunkGenerationStatus)index;
                }

                return null;
            }
        }
    }

    /// <summary>
    /// Revision of the currently retained generation products. Dependents must
    /// reject a successful future result when its revision no longer matches.
    /// </summary>
    public ulong GenerationProgressRevision
    {
        get
        {
            lock (_stateGate)
                return _generationProgressRevision;
        }
    }

    public ulong TicketLevelRevision { get; private set; }

    internal virtual bool UpdateTicketLevel(int ticketLevel)
    {
        ValidateTicketLevel(ticketLevel);
        lock (_stateGate)
        {
            if (TicketLevel == ticketLevel && TicketLevelRevision != 0)
                return false;

            TicketLevel = ticketLevel;
            QueuePriority = QueuePriority with { TicketLevel = ticketLevel };
            AdvanceQueuePriorityRevision();
            HighestAllowedGenerationStatus =
                ChunkLevel.GetGenerationStatus(ticketLevel);
            TicketLevelRevision++;

            ReconcileGenerationFutures();
            foreach (var section in _sectionGenerationProgress.Values)
                section.Reconcile(HighestAllowedGenerationStatus, TicketLevel);
            return true;
        }
    }

    /// <summary>
    /// Registers the sole active, column-scoped generation task. The holder
    /// rejects tasks outside its column, section-scoped/auxiliary tasks, work
    /// above its ticket-derived ceiling, and concurrent replacement tasks.
    /// </summary>
    public bool TryRegisterGenerationTask(
        ChunkGenerationTaskInstance task,
        ChunkGenerationStatus targetStatus)
    {
        task.Validate();
        if (task.Key.Column != Position ||
            task.Key.TargetStep != ChunkGenerationStepId.SectionReady ||
            task.Key.SectionY is not null)
            throw new ArgumentException(
                "A holder can own only its column-scoped SectionReady generation task.",
                nameof(task));

        ValidateGenerationStatus(targetStatus);
        lock (_stateGate)
        {
            if (targetStatus > HighestAllowedGenerationStatus ||
                _generationTask is not null)
                return false;

            // Scheduling a holder task is itself a status request, just as
            // scheduleChunkGenerationTask calls getOrCreateFuture in Minecraft.
            var targetFuture = GetOrCreateGenerationFuture(targetStatus);
            if (targetFuture.Task.IsCompletedSuccessfully &&
                targetFuture.Task.Result.Succeeded)
                return false;

            _generationTask = task;
            _generationTaskTargetStatus = targetStatus;
            return true;
        }
    }

    /// <summary>
    /// Releases the active task only when the exact execution identity matches.
    /// A late completion from a retired generation cannot clear its replacement.
    /// </summary>
    public bool RemoveGenerationTask(ChunkGenerationTaskInstance task)
    {
        task.Validate();
        if (task.Key.Column != Position)
            throw new ArgumentException(
                "Generation task belongs to a different holder column.",
                nameof(task));
        lock (_stateGate)
        {
            if (_generationTask != task)
                return false;

            _generationTask = null;
            _generationTaskTargetStatus = ChunkGenerationStatus.Unloaded;
            return true;
        }
    }

    /// <summary>
    /// Replaces the holder's live queue key after the residency scheduler
    /// supplies the current spatial context. Ticket urgency must match the
    /// holder's authoritative ticket level.
    /// </summary>
    internal bool UpdateQueuePriority(ChunkGenerationTaskPriority priority)
    {
        if (priority.TicketLevel != TicketLevel)
            throw new ArgumentException(
                "Queue priority ticket level must match the holder ticket level.",
                nameof(priority));
        if (priority.HorizontalDistanceSquared < 0 ||
            priority.TotalDistanceSquared < 0)
            throw new ArgumentOutOfRangeException(nameof(priority));

        if (HasQueuePriorityContext && QueuePriority == priority)
            return false;

        QueuePriority = priority;
        HasQueuePriorityContext = true;
        AdvanceQueuePriorityRevision();
        return true;
    }

    private void AdvanceQueuePriorityRevision()
    {
        if (QueuePriorityRevision == ulong.MaxValue)
            throw new InvalidOperationException(
                "Chunk holder queue priority revision space was exhausted.");
        QueuePriorityRevision++;
    }

    /// <summary>
    /// Returns the future for one requested generation status. The ticket level
    /// is only a ceiling: a permitted status may still be incomplete.
    /// </summary>
    public Task<ChunkGenerationFutureResult> GetGenerationFuture(
        ChunkGenerationStatus requestedStatus)
    {
        ValidateGenerationStatus(requestedStatus);
        lock (_stateGate)
        {
            if (requestedStatus > HighestAllowedGenerationStatus)
            {
                return Task.FromResult(ChunkGenerationFutureResult.Unloaded(
                    requestedStatus,
                    $"Ticket level {TicketLevel} does not permit {requestedStatus}.",
                    _generationProgressRevision));
            }

            return GetOrCreateGenerationFuture(requestedStatus).Task;
        }
    }

    /// <summary>Gets the status future for one exact vertical section in this holder.</summary>
    public Task<ChunkGenerationFutureResult> GetSectionGenerationFuture(
        ChunkCoord section,
        ChunkGenerationStatus requestedStatus)
    {
        ValidateSection(section);
        ValidateGenerationStatus(requestedStatus);
        lock (_stateGate)
            return GetOrCreateSectionProgress(section).GetFuture(
                requestedStatus,
                HighestAllowedGenerationStatus,
                TicketLevel);
    }

    /// <summary>Highest achieved generation status retained for one exact section.</summary>
    public ChunkGenerationStatus GetCompletedSectionGenerationStatus(
        ChunkCoord section)
    {
        ValidateSection(section);
        lock (_stateGate)
            return _sectionGenerationProgress.TryGetValue(section, out var progress)
                ? progress.CompletedStatus
                : ChunkGenerationStatus.Unloaded;
    }

    public ulong GetSectionGenerationRevision(ChunkCoord section)
    {
        ValidateSection(section);
        lock (_stateGate)
            return _sectionGenerationProgress.TryGetValue(section, out var progress)
                ? progress.Revision
                : 1UL;
    }

    public bool CompleteSectionGenerationStatus(
        ChunkCoord section,
        ChunkGenerationStatus completedStatus,
        Chunk? product = null)
    {
        ValidateSection(section);
        ValidateGenerationStatus(completedStatus);
        lock (_stateGate)
            return GetOrCreateSectionProgress(section).Complete(
                completedStatus,
                HighestAllowedGenerationStatus,
                TicketLevel,
                product);
    }

    public bool ResetSectionGenerationProgress(
        ChunkCoord section,
        string reason)
    {
        ValidateSection(section);
        lock (_stateGate)
        {
            if (!_sectionGenerationProgress.TryGetValue(section, out var progress) ||
                !progress.HasProgressOrCompletedFuture)
                return false;

            progress.Reset(reason);
            return true;
        }
    }

    /// <summary>
    /// Completes a generation status and every earlier status future. This
    /// reports actual achieved progress; it does not change the ticket ceiling.
    /// </summary>
    public bool CompleteGenerationStatus(
        ChunkGenerationStatus completedStatus)
    {
        ValidateGenerationStatus(completedStatus);
        lock (_stateGate)
        {
            if (completedStatus > CompletedGenerationStatus)
                CompletedGenerationStatus = completedStatus;

            ReconcileGenerationFutures();
            return completedStatus <= HighestAllowedGenerationStatus;
        }
    }

    /// <summary>
    /// Invalidates the current generation request even when no stage has
    /// completed yet. In-flight tasks capture this revision at admission, so
    /// changing the holder's required target set must stale those tasks too.
    /// </summary>
    public bool InvalidateGenerationRequest(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_stateGate)
        {
            if (ResetGenerationProgressCore(reason))
                return true;

            if (_generationProgressRevision == ulong.MaxValue)
                throw new InvalidOperationException(
                    $"Generation request revision space was exhausted: {Position}");

            _generationProgressRevision++;
            return true;
        }
    }

    /// <summary>
    /// Invalidates partial products discarded after a failed/cancelled
    /// generation attempt. Existing waiters are released with a failed result;
    /// future requests receive fresh tasks under the new progress revision.
    /// </summary>
    protected bool ResetGenerationProgressCore(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        lock (_stateGate)
        {
            if (CompletedGenerationStatus == ChunkGenerationStatus.Unloaded &&
                _generationFutures.All(future =>
                    future is null || !future.Task.IsCompleted) &&
                _sectionGenerationProgress.Values.All(progress =>
                    !progress.HasProgressOrCompletedFuture))
                return false;
            if (_generationProgressRevision == ulong.MaxValue)
                throw new InvalidOperationException(
                    $"Generation progress revision space was exhausted: {Position}");

            var failedRevision = _generationProgressRevision;
            _generationProgressRevision++;
            CompletedGenerationStatus = ChunkGenerationStatus.Unloaded;
            for (var index = 1; index < _generationFutures.Length; index++)
            {
                var future = _generationFutures[index];
                if (future is null)
                    continue;
                future.TrySetResult(ChunkGenerationFutureResult.Unloaded(
                    (ChunkGenerationStatus)index, reason, failedRevision));
                _generationFutures[index] = null;
            }
            foreach (var section in _sectionGenerationProgress.Values)
                section.Reset(reason);

            return true;
        }
    }

    // Caller holds _stateGate. Each status has one stable slot, matching the
    // status-indexed AtomicReferenceArray used by GenerationChunkHolder 26.3.
    private TaskCompletionSource<ChunkGenerationFutureResult>
        GetOrCreateGenerationFuture(ChunkGenerationStatus status)
    {
        var index = (int)status;
        var future = _generationFutures[index];
        if (future is not null)
            return future;

        future = NewGenerationFuture();
        if (status <= CompletedGenerationStatus)
            future.TrySetResult(ChunkGenerationFutureResult.Success(
                status, _generationProgressRevision));
        _generationFutures[index] = future;
        return future;
    }

    private ChunkSectionGenerationProgressState GetOrCreateSectionProgress(
        ChunkCoord section)
    {
        if (_sectionGenerationProgress.TryGetValue(section, out var progress))
            return progress;

        progress = new ChunkSectionGenerationProgressState();
        progress.Reconcile(HighestAllowedGenerationStatus, TicketLevel);
        _sectionGenerationProgress.Add(section, progress);
        return progress;
    }

    private void ValidateSection(ChunkCoord section)
    {
        if (section.Y < 0)
            throw new ArgumentOutOfRangeException(
                nameof(section), "Sphere chunk Y cannot be negative.");
        if (ChunkColumnCoord.FromChunk(section) != Position)
            throw new ArgumentException(
                $"Section {section} does not belong to holder {Position}.",
                nameof(section));
    }

    private static void ValidateTicketLevel(int ticketLevel)
    {
        if (ticketLevel < 0 || ticketLevel > ChunkLevel.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(ticketLevel));
    }

    private static void ValidateGenerationStatus(
        ChunkGenerationStatus status)
    {
        if (!Enum.IsDefined(status) || status == ChunkGenerationStatus.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(status));
    }

    private void ReconcileGenerationFutures()
    {
        lock (_stateGate)
        {
            for (var index = 1; index < _generationFutures.Length; index++)
            {
                var future = _generationFutures[index];
                if (future is null)
                    continue;

                var status = (ChunkGenerationStatus)index;
                if (status > HighestAllowedGenerationStatus)
                {
                    future.TrySetResult(ChunkGenerationFutureResult.Unloaded(
                        status,
                        $"Ticket level {TicketLevel} does not permit {status}.",
                        _generationProgressRevision));
                    _generationFutures[index] = null;
                    continue;
                }

                if (status <= CompletedGenerationStatus)
                {
                    if (future.Task.IsCompletedSuccessfully &&
                        !future.Task.Result.Succeeded)
                    {
                        future = NewGenerationFuture();
                        _generationFutures[index] = future;
                    }

                    future.TrySetResult(ChunkGenerationFutureResult.Success(
                        status, _generationProgressRevision));
                }
                else if (future.Task.IsCompletedSuccessfully &&
                         !future.Task.Result.Succeeded)
                {
                    _generationFutures[index] = NewGenerationFuture();
                }
            }
        }
    }

    private static TaskCompletionSource<ChunkGenerationFutureResult>
        NewGenerationFuture() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
