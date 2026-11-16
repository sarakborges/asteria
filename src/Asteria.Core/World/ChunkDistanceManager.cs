namespace Asteria.Core.World;

/// <summary>
/// Owns the ticket store and independent loading/simulation distance graphs.
/// The holder map has a separate owner; loading updates reconcile it while the
/// simulation graph remains an independent gate for world ticking.
/// </summary>
public sealed class ChunkDistanceManager
{
    private readonly ChunkTicketStorage _tickets = new();
    private readonly ChunkDistanceTracker _loading = new();
    private readonly ChunkDistanceTracker _simulation = new();
    private readonly NaturalSpawnChunkTracker _naturalSpawn = new();
    private readonly ChunkHolderMap _holders;
    private readonly Dictionary<ChunkColumnCoord, int> _pendingLoadingSourceChanges = [];
    private readonly Dictionary<ChunkColumnCoord, int> _pendingSimulationSourceChanges = [];

    public ChunkDistanceManager()
    {
        _holders = new ChunkHolderMap(_loading.Levels);
        _tickets.LoadingChunkUpdated +=
            (column, level, _) => _pendingLoadingSourceChanges[column] = level;
        _tickets.SimulationChunkUpdated +=
            (column, level, _) => _pendingSimulationSourceChanges[column] = level;
    }

    /// <summary>
    /// Raised once per settled loading-graph update when one or more holder
    /// ticket levels change. Subscribers reclassify queued work from the new
    /// authoritative levels rather than relying on distance estimates.
    /// </summary>
    public event Action<IReadOnlyList<ChunkHolderTicketLevelChange>>?
        HolderTicketLevelsChanged;

    public ChunkTicketStorage Tickets => _tickets;

    public ChunkDistanceTracker LoadingTracker => _loading;

    public ChunkDistanceTracker SimulationTracker => _simulation;

    public NaturalSpawnChunkTracker NaturalSpawnTracker => _naturalSpawn;

    public bool IsNaturalSpawnCandidate(ChunkColumnCoord column) =>
        _naturalSpawn.Contains(column);

    public IReadOnlyCollection<ChunkHolder> Holders => _holders.Values;

    public int HolderCount => _holders.Count;

    public IReadOnlyCollection<ChunkColumnCoord> PendingUnloadColumns =>
        _holders.PendingUnloadColumns;

    public int PendingUnloadCount => _holders.PendingUnloadCount;

    public bool IsHolderPendingUnload(ChunkColumnCoord column) =>
        _holders.IsPendingUnload(column);

    public bool CompletePendingHolderUnload(ChunkColumnCoord column) =>
        _holders.CompletePendingUnload(column);

    public ChunkHolder? GetHolder(ChunkColumnCoord column) =>
        _holders.Get(column);

    public bool TryGetHolder(
        ChunkColumnCoord column,
        out ChunkHolder holder) =>
        _holders.TryGet(column, out holder!);

    /// <summary>
    /// Synchronizes the desired player view and simulation source. Existing
    /// PLAYER_LOADING tickets remain for columns still in view; new loading
    /// tickets are admitted later by the four-slot player-ticket dispatcher.
    /// </summary>
    public bool UpdatePlayerView(
        IEnumerable<ChunkColumnCoord> desiredColumns,
        ChunkColumnCoord playerColumn,
        int simulationDistance)
    {
        ArgumentNullException.ThrowIfNull(desiredColumns);
        if (simulationDistance < 0 ||
            simulationDistance >= ChunkLevel.EntityTicking)
            throw new ArgumentOutOfRangeException(nameof(simulationDistance));

        var desired = desiredColumns.ToHashSet();
        var naturalSpawnChanged = _naturalSpawn.UpdateCenter(playerColumn);
        var loadingChanged = _tickets.RemoveTicketsWhere(
            ChunkTicketType.PlayerLoading,
            column => !desired.Contains(column));
        var simulationChanged = _tickets.ReplaceTickets(
            ChunkTicketType.PlayerSimulation,
            [
                new ChunkTicketPlacement(
                    playerColumn,
                    new ChunkTicket(
                        ChunkTicketType.PlayerSimulation,
                        ChunkLevel.EntityTicking - simulationDistance)),
            ]);
        if (loadingChanged || simulationChanged)
            RebuildDistanceGraphs(loadingChanged, simulationChanged);

        return loadingChanged || simulationChanged || naturalSpawnChanged;
    }

    public bool AddPlayerLoadingTicket(ChunkColumnCoord column)
    {
        if (!_tickets.AddTicket(
                column,
                new ChunkTicket(
                    ChunkTicketType.PlayerLoading,
                    ChunkLevel.EntityTicking)))
            return false;

        RebuildDistanceGraphs(
            loadingChanged: true,
            simulationChanged: false);
        return true;
    }

    public bool RemovePlayerLoadingTicket(ChunkColumnCoord column)
    {
        if (!_tickets.RemoveTicket(
                column,
                new ChunkTicket(
                    ChunkTicketType.PlayerLoading,
                    ChunkLevel.EntityTicking)))
            return false;

        RebuildDistanceGraphs(
            loadingChanged: true,
            simulationChanged: false);
        return true;
    }

    public bool HasPlayerLoadingTicket(ChunkColumnCoord column) =>
        _tickets.GetTickets(column).Contains(
            new ChunkTicket(
                ChunkTicketType.PlayerLoading,
                ChunkLevel.EntityTicking));

    /// <summary>
    /// Adds any authored ticket identity and propagates only the distance
    /// graphs named by its flags. Time-limited ticket types receive their
    /// expiry from the storage's per-ticket timeout policy.
    /// </summary>
    public bool AddTicket(
        ChunkColumnCoord column,
        ChunkTicket ticket,
        ulong currentTick)
    {
        if (!_tickets.AddTicket(column, ticket, currentTick))
            return false;

        var flags = ticket.Flags;
        RebuildDistanceGraphs(
            (flags & ChunkTicketFlags.Loading) != 0,
            (flags & ChunkTicketFlags.Simulation) != 0);
        return true;
    }

    public bool RemoveTicket(
        ChunkColumnCoord column,
        ChunkTicket ticket)
    {
        if (!_tickets.RemoveTicket(column, ticket))
            return false;

        var flags = ticket.Flags;
        RebuildDistanceGraphs(
            (flags & ChunkTicketFlags.Loading) != 0,
            (flags & ChunkTicketFlags.Simulation) != 0);
        return true;
    }

    /// <summary>
    /// Advances ticket lifetimes at the current world tick. Countdown behavior
    /// depends on ticket type and holder save readiness; only expired ticket
    /// flags cause distance-graph propagation.
    /// </summary>
    public ChunkTicketFlags AdvanceTick(ulong currentTick)
    {
        var changed = _tickets.ExpireTickets(
            currentTick,
            _holders.IsReadyForSaving);
        if (changed == ChunkTicketFlags.None)
            return changed;

        RebuildDistanceGraphs(
            (changed & ChunkTicketFlags.Loading) != 0,
            (changed & ChunkTicketFlags.Simulation) != 0);
        return changed;
    }

    /// <summary>
    /// Retains every horizontal holder intersecting the generation plan's
    /// spatial dependency envelope. The lease is released when its owning
    /// ChunkGenerationTask reaches a terminal state.
    /// </summary>
    public ChunkDependencyClaim AcquireDependencyClaims(
        ChunkCoord target,
        ChunkGenerationDependencyEnvelope envelope) =>
        _holders.AcquireDependencyClaims([target], envelope);

    public ChunkDependencyClaim AcquireDependencyClaims(
        IEnumerable<ChunkCoord> targets,
        ChunkGenerationDependencyEnvelope envelope) =>
        _holders.AcquireDependencyClaims(targets, envelope);

    private void RebuildDistanceGraphs(
        bool loadingChanged,
        bool simulationChanged)
    {
        if (loadingChanged)
        {
            var changes = _loading.MaximumLevel != ChunkLevel.MaximumLoading
                ? _loading.SynchronizeSourcesWithChanges(
                    _tickets,
                    ChunkTicketFlags.Loading,
                    ChunkLevel.MaximumLoading)
                : _loading.ApplySourceChanges(
                    DrainSourceChanges(_pendingLoadingSourceChanges),
                    ChunkLevel.MaximumLoading);
            _pendingLoadingSourceChanges.Clear();
            SynchronizeHolders(changes);
        }

        if (simulationChanged)
        {
            if (_simulation.MaximumLevel != ChunkLevel.BlockTicking)
            {
                _simulation.SynchronizeSources(
                    _tickets,
                    ChunkTicketFlags.Simulation,
                    ChunkLevel.BlockTicking);
                _pendingSimulationSourceChanges.Clear();
            }
            else
            {
                _simulation.ApplySourceChanges(
                    DrainSourceChanges(_pendingSimulationSourceChanges),
                    ChunkLevel.BlockTicking);
            }
        }
    }

    private static IReadOnlyList<KeyValuePair<ChunkColumnCoord, int>>
        DrainSourceChanges(
            Dictionary<ChunkColumnCoord, int> pending)
    {
        if (pending.Count == 0)
            return Array.Empty<KeyValuePair<ChunkColumnCoord, int>>();

        var changes = pending
            .OrderBy(entry => entry.Key.X)
            .ThenBy(entry => entry.Key.Z)
            .ToArray();
        pending.Clear();
        return changes;
    }

    private void SynchronizeHolders(
        IReadOnlyList<ChunkDistanceLevelChange> graphChanges)
    {
        var changes = _holders.SynchronizeLoadingLevels(graphChanges);
        if (changes.Count > 0)
            HolderTicketLevelsChanged?.Invoke(changes);
    }

    public int PersistentTicketCount =>
        _tickets.PersistentTicketCount;

    public int PersistentTimedTicketCount =>
        _tickets.PersistentTimedTicketCount;

    public PersistentChunkTicketSnapshot CapturePersistentTickets() =>
        _tickets.CapturePersistentTickets();

    public void RestorePersistentTickets(
        PersistentChunkTicketSnapshot snapshot,
        ulong currentTick)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _tickets.RestorePersistentTickets(snapshot, currentTick);
        if (snapshot.Entries.Count > 0)
            RebuildDistanceGraphs(
                loadingChanged: true,
                simulationChanged: true);
    }

    public bool CompleteFullStatus(
        ChunkColumnCoord column,
        ChunkFullStatus status) =>
        _holders.CompleteFullStatus(column, status);

    public Task<ChunkGenerationFutureResult> GetGenerationFuture(
        ChunkColumnCoord column,
        ChunkGenerationStatus requestedStatus) =>
        _holders.GetGenerationFuture(column, requestedStatus);

    public bool CompleteGenerationStatus(
        ChunkColumnCoord column,
        ChunkGenerationStatus status) =>
        _holders.CompleteGenerationStatus(column, status);

    public Task<ChunkGenerationFutureResult> GetSectionGenerationFuture(
        ChunkCoord section,
        ChunkGenerationStatus requestedStatus) =>
        _holders.GetSectionGenerationFuture(section, requestedStatus);

    public ChunkGenerationStatus GetCompletedSectionGenerationStatus(
        ChunkCoord section) =>
        _holders.GetCompletedSectionGenerationStatus(section);

    public ulong GetSectionGenerationRevision(ChunkCoord section) =>
        _holders.GetSectionGenerationRevision(section);

    public bool CompleteSectionGenerationStatus(
        ChunkCoord section,
        ChunkGenerationStatus status,
        Chunk? product = null) =>
        _holders.CompleteSectionGenerationStatus(section, status, product);

    public bool ResetSectionGenerationProgress(
        ChunkCoord section,
        string reason) =>
        _holders.ResetSectionGenerationProgress(section, reason);

    public ulong GetGenerationProgressRevision(ChunkColumnCoord column) =>
        _holders.GetGenerationProgressRevision(column);

    public bool ResetGenerationProgress(
        ChunkColumnCoord column,
        string reason) =>
        _holders.ResetGenerationProgress(column, reason);

    public bool InvalidateGenerationRequest(
        ChunkColumnCoord column,
        string reason) =>
        _holders.InvalidateGenerationRequest(column, reason);

    public bool FailPendingPromotions(
        ChunkColumnCoord column,
        string reason) =>
        _holders.FailPendingPromotions(column, reason);

    public int GetLoadingLevel(ChunkColumnCoord column) =>
        _loading.GetLevel(column, ChunkLevel.Unloaded);

    public int GetSimulationLevel(ChunkColumnCoord column) =>
        _simulation.GetLevel(column, ChunkLevel.NoSimulationTicket);

    public ChunkFullStatus GetFullStatus(ChunkColumnCoord column) =>
        ChunkLevel.GetFullStatus(GetLoadingLevel(column));

    public ChunkGenerationStatus GetGenerationStatus(ChunkColumnCoord column) =>
        ChunkLevel.GetGenerationStatus(GetLoadingLevel(column));

    public bool IsBlockTicking(ChunkColumnCoord column) =>
        GetSimulationLevel(column) <= ChunkLevel.BlockTicking;

    public bool IsEntityTicking(ChunkColumnCoord column) =>
        GetSimulationLevel(column) <= ChunkLevel.EntityTicking;
}
