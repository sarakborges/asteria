namespace Asteria.Core.World;

/// <summary>
/// Owns the identity and lifetime of horizontal chunk holders. Loading graph
/// updates are applied here; generation tasks retain holders through explicit
/// dependency claims until their work reaches a terminal state.
/// </summary>
public sealed class ChunkHolderMap
{
    private readonly Dictionary<ChunkColumnCoord, ChunkHolder> _holders = [];
    private readonly IReadOnlyDictionary<ChunkColumnCoord, int> _loadingLevels;

    internal ChunkHolderMap(
        IReadOnlyDictionary<ChunkColumnCoord, int> loadingLevels)
    {
        _loadingLevels = loadingLevels ??
            throw new ArgumentNullException(nameof(loadingLevels));
    }

    public IReadOnlyCollection<ChunkHolder> Values => _holders.Values;

    public int Count => _holders.Count;

    public ChunkHolder? Get(ChunkColumnCoord column) =>
        _holders.TryGetValue(column, out var holder) ? holder : null;

    public bool TryGet(
        ChunkColumnCoord column,
        out ChunkHolder holder) =>
        _holders.TryGetValue(column, out holder!);

    public bool IsReadyForSaving(ChunkColumnCoord column) =>
        !_holders.TryGetValue(column, out var holder) ||
        holder.IsReadyForSaving;

    /// <summary>
    /// Reconciles holder membership and ticket-derived generation ceilings
    /// with the settled loading graph. Dependency-only holders remain present
    /// but are demoted to UNLOADED until a graph ticket promotes them again.
    /// </summary>
    internal void SynchronizeLoadingLevels()
    {
        foreach (var (column, level) in _loadingLevels)
        {
            if (_holders.TryGetValue(column, out var holder))
                holder.UpdateTicketLevel(level);
            else
                _holders.Add(column, new ChunkHolder(column, level));
        }

        foreach (var (column, holder) in _holders.ToArray())
        {
            if (_loadingLevels.TryGetValue(column, out var level))
            {
                holder.UpdateTicketLevel(level);
                continue;
            }

            holder.UpdateTicketLevel(ChunkLevel.Unloaded);
            if (holder.DependencyClaimCount == 0)
                _holders.Remove(column);
        }
    }

    /// <summary>
    /// Retains every horizontal holder intersecting the generation plan's
    /// spatial dependency envelope. Each holder is retained once even when
    /// several section targets or overlapping envelopes refer to it.
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
            _holders.Remove(column);
        }
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