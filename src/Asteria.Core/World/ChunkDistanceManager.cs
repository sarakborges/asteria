namespace Asteria.Core.World;

/// <summary>
/// Owns the ticket store, independent loading and simulation distance graphs,
/// and the holder map produced by the loading graph. Player view tickets decide
/// which holders exist and their generation ceiling; simulation tickets decide
/// which loaded holders are allowed to tick.
/// </summary>
public sealed class ChunkDistanceManager
{
    private readonly ChunkTicketStorage _tickets = new();
    private readonly ChunkDistanceTracker _loading = new();
    private readonly ChunkDistanceTracker _simulation = new();
    private readonly NaturalSpawnChunkTracker _naturalSpawn = new();
    private readonly Dictionary<ChunkColumnCoord, ChunkHolder> _holders = [];

    public ChunkTicketStorage Tickets => _tickets;

    public ChunkDistanceTracker LoadingTracker => _loading;

    public ChunkDistanceTracker SimulationTracker => _simulation;

    public NaturalSpawnChunkTracker NaturalSpawnTracker => _naturalSpawn;

    public bool IsNaturalSpawnCandidate(ChunkColumnCoord column) =>
        _naturalSpawn.Contains(column);

    public IReadOnlyCollection<ChunkHolder> Holders => _holders.Values;

    public int HolderCount => _holders.Count;

    public ChunkHolder? GetHolder(ChunkColumnCoord column) =>
        _holders.TryGetValue(column, out var holder) ? holder : null;

    public bool TryGetHolder(
        ChunkColumnCoord column,
        out ChunkHolder holder) =>
        _holders.TryGetValue(column, out holder!);

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

        _loading.SynchronizeSources(
            _tickets,
            ChunkTicketFlags.Loading,
            ChunkLevel.MaximumLoading);
        SynchronizeHolders();
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

        _loading.SynchronizeSources(
            _tickets,
            ChunkTicketFlags.Loading,
            ChunkLevel.MaximumLoading);
        SynchronizeHolders();
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
            column => !_holders.TryGetValue(column, out var holder) ||
                holder.IsReadyForSaving);
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
        AcquireDependencyClaims([target], envelope);

    /// <summary>
    /// Acquires the union of several section dependency envelopes while
    /// retaining each horizontal holder exactly once. A column generation
    /// task uses this for every vertical section it owns.
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

        // A holder is retained once regardless of how many vertical sections
        // or overlapping envelopes inside this column refer to it.
        foreach (var column in columns)
            _holders[column].AddDependencyClaim();

        return new ChunkDependencyClaim(
            this,
            columns.OrderBy(column => column.X).ThenBy(column => column.Z).ToArray(),
            sectionTargets
                .OrderBy(section => section.Y)
                .ThenBy(section => section.Z)
                .ThenBy(section => section.X)
                .ToArray());
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
                _loading.Levels.ContainsKey(column))
                continue;

            holder.UpdateTicketLevel(ChunkLevel.Unloaded);
            _holders.Remove(column);
        }
    }

    private void RebuildDistanceGraphs(
        bool loadingChanged,
        bool simulationChanged)
    {
        if (loadingChanged)
        {
            _loading.SynchronizeSources(
                _tickets,
                ChunkTicketFlags.Loading,
                ChunkLevel.MaximumLoading);
            SynchronizeHolders();
        }

        if (simulationChanged)
        {
            _simulation.SynchronizeSources(
                _tickets,
                ChunkTicketFlags.Simulation,
                ChunkLevel.BlockTicking);
        }
    }

    private void SynchronizeHolders()
    {
        foreach (var (column, level) in _loading.Levels)
        {
            if (_holders.TryGetValue(column, out var holder))
                holder.UpdateTicketLevel(level);
            else
                _holders.Add(column, new ChunkHolder(column, level));
        }

        foreach (var (column, holder) in _holders.ToArray())
        {
            if (_loading.Levels.TryGetValue(column, out var level))
            {
                holder.UpdateTicketLevel(level);
                continue;
            }

            // An un-ticketed holder remains alive while a generation task
            // claims it as a spatial dependency. The ticket ceiling is
            // demoted immediately; only holder lifetime is retained.
            holder.UpdateTicketLevel(ChunkLevel.Unloaded);
            if (holder.DependencyClaimCount == 0)
                _holders.Remove(column);
        }
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
