namespace Asteria.Core.World;

/// <summary>
/// One authoritative loading-ticket level transition emitted after the loading
/// graph settles and the owning holder reconciles its status futures.
/// </summary>
public readonly record struct ChunkHolderTicketLevelChange(
    ChunkHolder Holder,
    int PreviousTicketLevel,
    int TicketLevel);

/// <summary>
/// A holder's effective generation queue key changed. The revision lets
/// dispatch owners reject stale notifications without polling the holder map.
/// </summary>
public readonly record struct ChunkHolderQueuePriorityChange(
    ChunkHolder Holder,
    ChunkGenerationTaskPriority PreviousPriority,
    ChunkGenerationTaskPriority Priority,
    ulong Revision);

/// <summary>
/// Owns the identity and lifetime of horizontal chunk holders. Loading graph
/// updates are applied here; generation tasks retain holders through explicit
/// dependency claims until their work reaches a terminal state.
/// </summary>
public sealed class ChunkHolderMap
{
    private readonly Dictionary<ChunkColumnCoord, ChunkHolder> _holders = [];
    private readonly HashSet<ChunkColumnCoord> _pendingUnloads = [];
    private readonly IReadOnlyDictionary<ChunkColumnCoord, int> _loadingLevels;

    internal ChunkHolderMap(
        IReadOnlyDictionary<ChunkColumnCoord, int> loadingLevels)
    {
        _loadingLevels = loadingLevels ??
            throw new ArgumentNullException(nameof(loadingLevels));
    }

    public IReadOnlyCollection<ChunkHolder> Values => _holders.Values;

    public int Count => _holders.Count;

    public IReadOnlyCollection<ChunkColumnCoord> PendingUnloadColumns =>
        _pendingUnloads.ToArray();

    public int PendingUnloadCount => _pendingUnloads.Count;

    public bool IsPendingUnload(ChunkColumnCoord column) =>
        _pendingUnloads.Contains(column);

    public ChunkHolder? Get(ChunkColumnCoord column) =>
        _holders.TryGetValue(column, out var holder) ? holder : null;

    public bool TryGet(
        ChunkColumnCoord column,
        out ChunkHolder holder) =>
        _holders.TryGetValue(column, out holder!);

    public bool IsReadyForSaving(ChunkColumnCoord column) =>
        !_holders.TryGetValue(column, out var holder) ||
        holder.IsReadyForSaving;

    public event Action<ChunkHolderQueuePriorityChange>? QueuePriorityChanged;

    internal bool UpdateQueuePriority(
        ChunkColumnCoord column,
        ChunkGenerationTaskPriority priority)
    {
        if (!_holders.TryGetValue(column, out var holder))
            return false;

        var previousPriority = holder.QueuePriority;
        if (!holder.UpdateQueuePriority(priority))
            return false;

        PublishQueuePriorityChanged(holder, previousPriority);
        return true;
    }

    internal void UpdateQueuePriorities(
        Func<ChunkHolder, ChunkGenerationTaskPriority?> priorityFactory)
    {
        ArgumentNullException.ThrowIfNull(priorityFactory);
        foreach (var holder in _holders.Values
                     .OrderBy(holder => holder.Position.X)
                     .ThenBy(holder => holder.Position.Z))
        {
            if (priorityFactory(holder) is not { } priority)
                continue;

            var previousPriority = holder.QueuePriority;
            if (holder.UpdateQueuePriority(priority))
                PublishQueuePriorityChanged(holder, previousPriority);
        }
    }

    private void PublishQueuePriorityChanged(
        ChunkHolder holder,
        ChunkGenerationTaskPriority previousPriority)
    {
        QueuePriorityChanged?.Invoke(new ChunkHolderQueuePriorityChange(
            holder,
            previousPriority,
            holder.QueuePriority,
            holder.QueuePriorityRevision));
    }

    /// <summary>
    /// Reconciles holder membership and ticket-derived generation ceilings
    /// with the settled loading graph. Dependency-only holders remain present
    /// but are demoted to UNLOADED until a graph ticket promotes them again.
    /// </summary>
    internal IReadOnlyList<ChunkHolderTicketLevelChange> SynchronizeLoadingLevels(
        IReadOnlyList<ChunkDistanceLevelChange> graphChanges,
        Func<ChunkHolder, ChunkGenerationTaskPriority?> priorityFactory)
    {
        ArgumentNullException.ThrowIfNull(graphChanges);
        ArgumentNullException.ThrowIfNull(priorityFactory);
        if (graphChanges.Count == 0)
            return Array.Empty<ChunkHolderTicketLevelChange>();

        var changes = new List<ChunkHolderTicketLevelChange>(graphChanges.Count);
        foreach (var graphChange in graphChanges)
        {
            var column = graphChange.Column;
            if (_loadingLevels.TryGetValue(column, out var level))
            {
                _pendingUnloads.Remove(column);
                if (_holders.TryGetValue(column, out var holder))
                {
                    var previousLevel = holder.TicketLevel;
                    var previousHolderPriority = holder.QueuePriority;
                    if (holder.UpdateTicketLevel(level))
                    {
                        if (priorityFactory(holder) is { } priority)
                            holder.UpdateQueuePriority(priority);
                        changes.Add(new ChunkHolderTicketLevelChange(
                            holder, previousLevel, level));
                        PublishQueuePriorityChanged(holder, previousHolderPriority);
                    }
                }
                else
                {
                    var createdHolder = new ChunkHolder(column, level);
                    _holders.Add(column, createdHolder);
                    if (priorityFactory(createdHolder) is { } priority)
                    {
                        var previousPriority = createdHolder.QueuePriority;
                        createdHolder.UpdateQueuePriority(priority);
                        PublishQueuePriorityChanged(
                            createdHolder, previousPriority);
                    }
                    changes.Add(new ChunkHolderTicketLevelChange(
                        createdHolder, ChunkLevel.Unloaded, level));
                }

                continue;
            }

            if (!_holders.TryGetValue(column, out var unloadedHolder))
                continue;

            var previousTicketLevel = unloadedHolder.TicketLevel;
            var previousUnloadedPriority = unloadedHolder.QueuePriority;
            if (unloadedHolder.UpdateTicketLevel(ChunkLevel.Unloaded))
            {
                if (priorityFactory(unloadedHolder) is { } priority)
                    unloadedHolder.UpdateQueuePriority(priority);
                changes.Add(new ChunkHolderTicketLevelChange(
                    unloadedHolder, previousTicketLevel, ChunkLevel.Unloaded));
                PublishQueuePriorityChanged(unloadedHolder, previousUnloadedPriority);
            }
            _pendingUnloads.Add(column);
        }

        return changes;
    }

    /// <summary>
    /// Retains every horizontal holder intersecting the loading plan's
    /// holder-status envelope. Immutable generator-field read halos do not
    /// retain holders. Each holder is claimed once even when several section
    /// targets or overlapping envelopes refer to it.
    /// </summary>
    public ChunkDependencyClaim AcquireDependencyClaims(
        IEnumerable<ChunkCoord> targets,
        ChunkGenerationDependencyEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (envelope.HorizontalRadius < 0 ||
            envelope.MinimumSectionOffset > envelope.MaximumSectionOffset)
            throw new ArgumentOutOfRangeException(nameof(envelope));

        var targetSections = targets.Distinct().ToArray();
        if (targetSections.Length == 0)
            throw new ArgumentException(
                "At least one target section is required for a dependency claim.",
                nameof(targets));
        if (targetSections.Any(section => section.Y < 0))
            throw new ArgumentOutOfRangeException(
                nameof(targets), "Sphere chunk Y cannot be negative.");

        var columns = new HashSet<ChunkColumnCoord>();
        var sectionTargets = new HashSet<ChunkCoord>();
        foreach (var target in targetSections)
        {
            for (var dz = -envelope.HorizontalRadius;
                 dz <= envelope.HorizontalRadius;
                 dz++)
            for (var dx = -envelope.HorizontalRadius;
                 dx <= envelope.HorizontalRadius;
                 dx++)
            {
                var x = (long)target.X + dx;
                var z = (long)target.Z + dz;
                if (x < int.MinValue || x > int.MaxValue ||
                    z < int.MinValue || z > int.MaxValue)
                    continue;

                var column = ChunkColumnCoord.FromChunk(
                    new ChunkCoord((int)x, 0, (int)z));
                if (!_holders.ContainsKey(column))
                    _holders.Add(column, new ChunkHolder(
                        column, ChunkLevel.Unloaded));
                if (_loadingLevels.ContainsKey(column))
                    _pendingUnloads.Remove(column);
                else
                    _pendingUnloads.Add(column);
                columns.Add(column);

                for (var sectionOffset = envelope.MinimumSectionOffset;
                     sectionOffset <= envelope.MaximumSectionOffset;
                     sectionOffset++)
                {
                    var sectionY = (long)target.Y + sectionOffset;
                    if (sectionY < 0 || sectionY > int.MaxValue)
                        continue;
                    sectionTargets.Add(new ChunkCoord(
                        (int)x, (int)sectionY, (int)z));
                }
            }
        }

        foreach (var column in columns)
            _holders[column].AddDependencyClaim();

        var orderedColumns = columns
            .OrderBy(column => column.X)
            .ThenBy(column => column.Z)
            .ToArray();
        var orderedSections = sectionTargets
            .OrderBy(section => section.Y)
            .ThenBy(section => section.Z)
            .ThenBy(section => section.X)
            .ToArray();

        return new ChunkDependencyClaim(this, orderedColumns, orderedSections);
    }

    internal void ReleaseDependencyClaims(
        IReadOnlyList<ChunkColumnCoord> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        foreach (var column in columns)
        {
            if (!_holders.TryGetValue(column, out var holder))
                throw new InvalidOperationException(
                    $"Claimed chunk holder was removed before its lease was released: {column}");

            holder.RemoveDependencyClaim();
            if (holder.DependencyClaimCount != 0 ||
                _loadingLevels.ContainsKey(column))
                continue;

            holder.UpdateTicketLevel(ChunkLevel.Unloaded);
            _pendingUnloads.Add(column);
        }
    }

    /// <summary>
    /// Acquires the horizontal holder neighborhood required by one Minecraft
    /// chunk step. The cache is centered on one X/Z column and retains each
    /// holder in the accumulated dependency radius exactly once.
    /// </summary>
    internal ChunkGenerationCache2D AcquireGenerationCache2D(
        ChunkColumnCoord center,
        ChunkStep step)
    {
        ArgumentNullException.ThrowIfNull(step);
        var radius = Math.Max(0, step.AccumulatedDependencies.Radius);
        var width = checked(radius * 2 + 1);
        var capacity = checked(width * width);
        var holders = new Dictionary<ChunkColumnCoord, ChunkHolder>(capacity);
        var claimedColumns = new List<ChunkColumnCoord>(capacity);
        ChunkDependencyClaim? claim = null;

        try
        {
            for (var dz = -radius; dz <= radius; dz++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = (long)center.X + dx;
                var z = (long)center.Z + dz;
                if (x < int.MinValue || x > int.MaxValue ||
                    z < int.MinValue || z > int.MaxValue)
                    throw new ArgumentOutOfRangeException(
                        nameof(center),
                        $"The dependency cache around {center} exceeds chunk-coordinate bounds.");

                var column = new ChunkColumnCoord((int)x, (int)z);
                if (!_holders.TryGetValue(column, out var holder))
                {
                    var ticketLevel = _loadingLevels.TryGetValue(column, out var level)
                        ? level
                        : ChunkLevel.Unloaded;
                    holder = new ChunkHolder(column, ticketLevel);
                    _holders.Add(column, holder);
                }

                if (_loadingLevels.ContainsKey(column))
                    _pendingUnloads.Remove(column);
                else
                    _pendingUnloads.Add(column);

                holder.AddDependencyClaim();
                claimedColumns.Add(column);
                holders.Add(column, holder);
            }

            claim = new ChunkDependencyClaim(
                this,
                claimedColumns
                    .OrderBy(column => column.X)
                    .ThenBy(column => column.Z)
                    .ToArray(),
                Array.Empty<ChunkCoord>());

            return new ChunkGenerationCache2D(
                center,
                radius,
                step,
                holders,
                claim);
        }
        catch
        {
            if (claim is not null)
                claim.Dispose();
            else if (claimedColumns.Count > 0)
                ReleaseDependencyClaims(claimedColumns);
            throw;
        }
    }

    /// <summary>
    /// Finishes the deferred unload only after the residency owner has
    /// archived every section in the column and no generation task retains
    /// the holder. A ticket arriving first reuses the same holder identity.
    /// </summary>

    public bool CompletePendingUnload(ChunkColumnCoord column)
    {
        if (!_holders.TryGetValue(column, out var holder))
        {
            _pendingUnloads.Remove(column);
            return false;
        }

        if (_loadingLevels.TryGetValue(column, out var level))
        {
            holder.UpdateTicketLevel(level);
            _pendingUnloads.Remove(column);
            return false;
        }

        if (holder.DependencyClaimCount != 0 ||
            !_pendingUnloads.Remove(column))
            return false;

        _holders.Remove(column);
        return true;
    }

    public bool CompleteFullStatus(
        ChunkColumnCoord column,
        ChunkFullStatus status) =>
        _holders.TryGetValue(column, out var holder) &&
        holder.CompleteFullStatus(status);

    public Task<ChunkGenerationFutureResult> GetGenerationFuture(
        ChunkColumnCoord column,
        ChunkGenerationStatus requestedStatus) =>
        _holders.TryGetValue(column, out var holder)
            ? holder.GetGenerationFuture(requestedStatus)
            : Task.FromResult(ChunkGenerationFutureResult.Unloaded(
                requestedStatus,
                $"Chunk holder does not exist: {column}"));

    public bool CompleteGenerationStatus(
        ChunkColumnCoord column,
        ChunkGenerationStatus status) =>
        _holders.TryGetValue(column, out var holder) &&
        holder.CompleteGenerationStatus(status);

    public Task<ChunkGenerationFutureResult> GetSectionGenerationFuture(
        ChunkCoord section,
        ChunkGenerationStatus requestedStatus)
    {
        ValidateSectionCoordinate(section);
        ValidateGenerationStatus(requestedStatus);
        var column = ChunkColumnCoord.FromChunk(section);
        return _holders.TryGetValue(column, out var holder)
            ? holder.GetSectionGenerationFuture(section, requestedStatus)
            : Task.FromResult(ChunkGenerationFutureResult.Unloaded(
                requestedStatus,
                $"Chunk holder does not exist for section {section}."));
    }

    public ChunkGenerationStatus GetCompletedSectionGenerationStatus(
        ChunkCoord section)
    {
        ValidateSectionCoordinate(section);
        return _holders.TryGetValue(
            ChunkColumnCoord.FromChunk(section), out var holder)
            ? holder.GetCompletedSectionGenerationStatus(section)
            : ChunkGenerationStatus.Unloaded;
    }

    public ulong GetSectionGenerationRevision(ChunkCoord section)
    {
        ValidateSectionCoordinate(section);
        return _holders.TryGetValue(
            ChunkColumnCoord.FromChunk(section), out var holder)
            ? holder.GetSectionGenerationRevision(section)
            : 0UL;
    }

    public bool CompleteSectionGenerationStatus(
        ChunkCoord section,
        ChunkGenerationStatus status,
        Chunk? product = null)
    {
        ValidateSectionCoordinate(section);
        return _holders.TryGetValue(
            ChunkColumnCoord.FromChunk(section), out var holder) &&
            holder.CompleteSectionGenerationStatus(section, status, product);
    }

    public bool ResetSectionGenerationProgress(
        ChunkCoord section,
        string reason)
    {
        ValidateSectionCoordinate(section);
        return _holders.TryGetValue(
            ChunkColumnCoord.FromChunk(section), out var holder) &&
            holder.ResetSectionGenerationProgress(section, reason);
    }

    public ulong GetGenerationProgressRevision(ChunkColumnCoord column) =>
        _holders.TryGetValue(column, out var holder)
            ? holder.GenerationProgressRevision
            : 0;

    public bool ResetGenerationProgress(
        ChunkColumnCoord column,
        string reason) =>
        _holders.TryGetValue(column, out var holder) &&
        holder.ResetGenerationProgress(reason);

    public bool InvalidateGenerationRequest(
        ChunkColumnCoord column,
        string reason) =>
        _holders.TryGetValue(column, out var holder) &&
        holder.InvalidateGenerationRequest(reason);

    public bool FailPendingPromotions(
        ChunkColumnCoord column,
        string reason) =>
        _holders.TryGetValue(column, out var holder) &&
        holder.FailPendingPromotions(reason);

    private static void ValidateSectionCoordinate(ChunkCoord section)
    {
        if (section.Y < 0)
            throw new ArgumentOutOfRangeException(
                nameof(section), "Sphere chunk Y cannot be negative.");
    }

    private static void ValidateGenerationStatus(ChunkGenerationStatus status)
    {
        if (!Enum.IsDefined(status) || status == ChunkGenerationStatus.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(status));
    }
}