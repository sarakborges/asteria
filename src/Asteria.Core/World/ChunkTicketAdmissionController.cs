namespace Asteria.Core.World;

/// <summary>
/// Throttles new player-loading ticket additions independently of worker
/// capacity. Vanilla Minecraft admits four new player tickets at a time and
/// releases each slot when its holder reaches the requested full status.
/// Asteria's current full status is a materialized set of vertical sections
/// within one horizontal column.
/// </summary>
public sealed class ChunkTicketAdmissionController
{
    public const int MaximumConcurrentPlayerTicketAdds = 4;

    private readonly ChunkDistanceManager _distanceManager;
    private readonly Dictionary<ChunkColumnCoord, HashSet<ChunkCoord>>
        _sectionsAwaitingFullStatus = [];

    public ChunkTicketAdmissionController(
        ChunkDistanceManager distanceManager)
    {
        _distanceManager = distanceManager ??
            throw new ArgumentNullException(nameof(distanceManager));
    }

    /// <summary>
    /// Number of player-loading ticket additions still waiting for their
    /// column to become fully materialized. This is deliberately not a
    /// worker count or a bound on already admitted tickets.
    /// </summary>
    public int PendingAdmissionCount => _sectionsAwaitingFullStatus.Count;

    public bool HasLoadingTicket(ChunkColumnCoord column) =>
        _distanceManager.HasPlayerLoadingTicket(column);

    /// <summary>
    /// Adds a player-loading ticket when a throttle slot is available.
    /// Already admitted columns do not consume another slot. Sections which
    /// are already resident do not delay the full-status completion signal.
    /// </summary>
    public bool TryAdmit(
        ChunkColumnCoord column,
        IEnumerable<ChunkCoord> requiredSections,
        IEnumerable<ChunkCoord> residentSections)
    {
        ArgumentNullException.ThrowIfNull(requiredSections);
        ArgumentNullException.ThrowIfNull(residentSections);

        if (_distanceManager.HasPlayerLoadingTicket(column) ||
            _sectionsAwaitingFullStatus.ContainsKey(column))
            return true;

        if (_sectionsAwaitingFullStatus.Count >= MaximumConcurrentPlayerTicketAdds)
            return false;

        _distanceManager.AddPlayerLoadingTicket(column);
        var resident = residentSections.ToHashSet();
        var pending = requiredSections
            .Where(coord => ChunkColumnCoord.FromChunk(coord) == column)
            .Where(coord => !resident.Contains(coord))
            .ToHashSet();

        if (pending.Count > 0)
            _sectionsAwaitingFullStatus.Add(column, pending);

        return true;
    }

    /// <summary>
    /// Reports that one vertical section reached full materialization. The
    /// ticket remains installed while the column stays in the player view;
    /// only the pending admission slot is released.
    /// </summary>
    public bool MarkSectionFull(ChunkCoord section)
    {
        var column = ChunkColumnCoord.FromChunk(section);
        if (!_sectionsAwaitingFullStatus.TryGetValue(column, out var pending) ||
            !pending.Remove(section))
            return false;

        if (pending.Count == 0)
            _sectionsAwaitingFullStatus.Remove(column);

        return true;
    }

    /// <summary>
    /// Releases a failed or cancelled full-status future without removing the
    /// loading ticket itself. A retry can continue under the existing ticket.
    /// </summary>
    public bool Abandon(ChunkColumnCoord column) =>
        _sectionsAwaitingFullStatus.Remove(column);

    /// <summary>
    /// Drops throttle bookkeeping for columns removed from the desired player
    /// view. ChunkDistanceManager owns removal of the actual loading tickets.
    /// </summary>
    public int RetainDesiredColumns(IReadOnlySet<ChunkColumnCoord> desiredColumns)
    {
        ArgumentNullException.ThrowIfNull(desiredColumns);
        var removed = 0;
        foreach (var column in _sectionsAwaitingFullStatus.Keys.ToArray())
        {
            if (desiredColumns.Contains(column))
                continue;
            if (_sectionsAwaitingFullStatus.Remove(column))
                removed++;
        }

        return removed;
    }
}
