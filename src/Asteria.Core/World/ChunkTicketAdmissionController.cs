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
        {
            _sectionsAwaitingFullStatus.Add(column, pending);
        }
        else
        {
            _distanceManager.CompleteFullStatus(
                column, ChunkFullStatus.EntityTicking);
        }

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
        {
            _sectionsAwaitingFullStatus.Remove(column);
            _distanceManager.CompleteFullStatus(
                column, ChunkFullStatus.EntityTicking);
        }

        return true;
    }

    /// <summary>
    /// Releases a failed or cancelled full-status future without removing the
    /// loading ticket itself. A retry can continue under the existing ticket.
    /// </summary>
    public bool Abandon(ChunkColumnCoord column)
    {
        if (!_sectionsAwaitingFullStatus.Remove(column))
            return false;

        _distanceManager.FailPendingPromotions(
            column, "The player chunk ticket could not reach ENTITY_TICKING.");
        return true;
    }

    /// <summary>
    /// Drops throttle bookkeeping for sections removed from the current
    /// desired view. ChunkDistanceManager separately removes loading tickets
    /// for columns which leave the horizontal view.
    /// </summary>
    public int RetainDesiredSections(IReadOnlySet<ChunkCoord> desiredSections)
    {
        ArgumentNullException.ThrowIfNull(desiredSections);
        var released = 0;
        foreach (var (column, pending) in _sectionsAwaitingFullStatus.ToArray())
        {
            pending.RemoveWhere(section => !desiredSections.Contains(section));
            if (pending.Count != 0)
                continue;

            _sectionsAwaitingFullStatus.Remove(column);
            _distanceManager.CompleteFullStatus(
                column, ChunkFullStatus.EntityTicking);
            released++;
        }

        return released;
    }
}
