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
    private readonly Dictionary<ChunkGenerationStatus,
        TaskCompletionSource<ChunkGenerationFutureResult>> _generationFutures =
        CreateGenerationFutures();
    private readonly Dictionary<ChunkCoord, ChunkSectionGenerationProgressState>
        _sectionGenerationProgress = [];

    protected GenerationChunkHolder(
        ChunkColumnCoord position,
        int ticketLevel)
    {
        ValidateTicketLevel(ticketLevel);
        Position = position;
        TicketLevel = ticketLevel;
        HighestAllowedGenerationStatus =
            ChunkLevel.GetGenerationStatus(ticketLevel);
        TicketLevelRevision = 1;
        ReconcileGenerationFutures();
    }

    public ChunkColumnCoord Position { get; }

    public int TicketLevel { get; private set; }

    public ChunkGenerationStatus HighestAllowedGenerationStatus { get; private set; }

    public ChunkGenerationStatus CompletedGenerationStatus { get; private set; }

    /// <summary>
    /// Revision of the currently retained generation products. Dependents must
    /// reject a successful future result when its revision no longer matches.
    /// </summary>
    public ulong GenerationProgressRevision { get; private set; } = 1;

    public ulong TicketLevelRevision { get; private set; }

    internal virtual bool UpdateTicketLevel(int ticketLevel)
    {
        ValidateTicketLevel(ticketLevel);
        if (TicketLevel == ticketLevel && TicketLevelRevision != 0)
            return false;

        TicketLevel = ticketLevel;
        HighestAllowedGenerationStatus =
            ChunkLevel.GetGenerationStatus(ticketLevel);
        TicketLevelRevision++;

        ReconcileGenerationFutures();
        foreach (var section in _sectionGenerationProgress.Values)
            section.Reconcile(HighestAllowedGenerationStatus, TicketLevel);
        return true;
    }

    /// <summary>
    /// Returns the future for one requested generation status. The ticket level
    /// is only a ceiling: a permitted status may still be incomplete.
    /// </summary>
    public Task<ChunkGenerationFutureResult> GetGenerationFuture(
        ChunkGenerationStatus requestedStatus)
    {
        ValidateGenerationStatus(requestedStatus);
        if (requestedStatus > HighestAllowedGenerationStatus)
        {
            return Task.FromResult(ChunkGenerationFutureResult.Unloaded(
                requestedStatus,
                $"Ticket level {TicketLevel} does not permit {requestedStatus}.",
                GenerationProgressRevision));
        }

        return _generationFutures[requestedStatus].Task;
    }

    /// <summary>Gets the status future for one exact vertical section in this holder.</summary>
    public Task<ChunkGenerationFutureResult> GetSectionGenerationFuture(
        ChunkCoord section,
        ChunkGenerationStatus requestedStatus)
    {
        ValidateSection(section);
        ValidateGenerationStatus(requestedStatus);
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
        return _sectionGenerationProgress.TryGetValue(section, out var progress)
            ? progress.CompletedStatus
            : ChunkGenerationStatus.Unloaded;
    }

    public ulong GetSectionGenerationRevision(ChunkCoord section)
    {
        ValidateSection(section);
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
        if (!_sectionGenerationProgress.TryGetValue(section, out var progress) ||
            !progress.HasProgressOrCompletedFuture)
            return false;

        progress.Reset(reason);
        return true;
    }

    /// <summary>
    /// Completes a generation status and every earlier status future. This
    /// reports actual achieved progress; it does not change the ticket ceiling.
    /// </summary>
    public bool CompleteGenerationStatus(
        ChunkGenerationStatus completedStatus)
    {
        ValidateGenerationStatus(completedStatus);

        if (completedStatus > CompletedGenerationStatus)
            CompletedGenerationStatus = completedStatus;

        ReconcileGenerationFutures();
        return completedStatus <= HighestAllowedGenerationStatus &&
            _generationFutures.Any(entry =>
                entry.Key <= completedStatus &&
                entry.Value.Task.IsCompletedSuccessfully &&
                entry.Value.Task.Result.Succeeded &&
                entry.Value.Task.Result.Revision == GenerationProgressRevision);
    }

    /// <summary>
    /// Invalidates the current generation request even when no stage has
    /// completed yet. In-flight tasks capture this revision at admission, so
    /// changing the holder's required target set must stale those tasks too.
    /// </summary>
    public bool InvalidateGenerationRequest(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (ResetGenerationProgressCore(reason))
            return true;

        if (GenerationProgressRevision == ulong.MaxValue)
            throw new InvalidOperationException(
                $"Generation request revision space was exhausted: {Position}");

        GenerationProgressRevision++;
        return true;
    }

    /// <summary>
    /// Invalidates partial products discarded after a failed/cancelled
    /// generation attempt. Existing waiters are released with a failed result;
    /// future requests receive fresh tasks under the new progress revision.
    /// </summary>
    protected bool ResetGenerationProgressCore(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (CompletedGenerationStatus == ChunkGenerationStatus.Unloaded &&
            _generationFutures.Values.All(future => !future.Task.IsCompleted) &&
            _sectionGenerationProgress.Values.All(progress =>
                !progress.HasProgressOrCompletedFuture))
            return false;
        if (GenerationProgressRevision == ulong.MaxValue)
            throw new InvalidOperationException(
                $"Generation progress revision space was exhausted: {Position}");

        var failedRevision = GenerationProgressRevision;
        GenerationProgressRevision++;
        CompletedGenerationStatus = ChunkGenerationStatus.Unloaded;
        foreach (var (status, future) in _generationFutures.ToArray())
        {
            future.TrySetResult(ChunkGenerationFutureResult.Unloaded(
                status, reason, failedRevision));
            _generationFutures[status] = NewGenerationFuture();
        }
        foreach (var section in _sectionGenerationProgress.Values)
            section.Reset(reason);

        return true;
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
        foreach (var status in _generationFutures.Keys.ToArray())
        {
            var future = _generationFutures[status];
            if (status > HighestAllowedGenerationStatus)
            {
                if (future.Task.IsCompletedSuccessfully &&
                    future.Task.Result.Succeeded)
                {
                    var demoted = NewGenerationFuture();
                    demoted.TrySetResult(ChunkGenerationFutureResult.Unloaded(
                        status,
                        $"Ticket level {TicketLevel} no longer permits {status}.",
                        GenerationProgressRevision));
                    _generationFutures[status] = demoted;
                }
                else
                {
                    future.TrySetResult(ChunkGenerationFutureResult.Unloaded(
                        status,
                        $"Ticket level {TicketLevel} does not permit {status}.",
                        GenerationProgressRevision));
                }
                continue;
            }

            if (status <= CompletedGenerationStatus)
            {
                if (future.Task.IsCompletedSuccessfully &&
                    !future.Task.Result.Succeeded)
                {
                    future = NewGenerationFuture();
                    _generationFutures[status] = future;
                }

                future.TrySetResult(ChunkGenerationFutureResult.Success(
                    status, GenerationProgressRevision));
            }
            else if (future.Task.IsCompletedSuccessfully &&
                     !future.Task.Result.Succeeded)
            {
                _generationFutures[status] = NewGenerationFuture();
            }
        }
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
