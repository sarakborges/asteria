namespace Asteria.Core.World;

/// <summary>Flags that determine which distance graph consumes a ticket.</summary>
[Flags]
public enum ChunkTicketFlags : byte
{
    None = 0,
    Loading = 1 << 0,
    Simulation = 1 << 1,
    Persist = 1 << 2,
    KeepDimensionActive = 1 << 3,
    CanExpireIfUnloaded = 1 << 4,
}

/// <summary>
/// Reasons that currently create persistent player interest in a chunk column.
/// Additional ticket types should be added with their actual gameplay owner.
/// </summary>
public enum ChunkTicketType : byte
{
    PlayerLoading,
    PlayerSimulation,
}

/// <summary>
/// Minecraft-style chunk status thresholds. Lower ticket levels represent
/// stronger interest; loading levels above Full continue through earlier
/// generation statuses without making the chunk accessible to gameplay.
/// </summary>
public static class ChunkLevel
{
    public const int EntityTicking = 31;
    public const int BlockTicking = 32;
    public const int Full = 33;
    public const int InitializeLight = 34;
    public const int Terrain = 35;
    public const int Biomes = 36;
    public const int StructureStarts = 37;

    // Minecraft 26.3 derives eleven dependency rings from its generation
    // pyramid: Full (33) plus eleven rings yields the last holder level (44).
    // Asteria will replace this reference radius with the accumulated spatial
    // envelope from its own ChunkGenerationPlan when that plan owns scheduling.
    public const int MaximumLoading = 44;
    public const int Unloaded = 45;
    public const int NoSimulationTicket = 33;

    public static ChunkFullStatus GetFullStatus(int level)
    {
        if (level <= EntityTicking)
            return ChunkFullStatus.EntityTicking;
        if (level == BlockTicking)
            return ChunkFullStatus.BlockTicking;
        if (level == Full)
            return ChunkFullStatus.Full;
        return ChunkFullStatus.Inaccessible;
    }

    public static ChunkGenerationStatus GetGenerationStatus(int level) =>
        level switch
        {
            <= Full => ChunkGenerationStatus.Full,
            InitializeLight => ChunkGenerationStatus.InitializeLight,
            Terrain => ChunkGenerationStatus.Terrain,
            Biomes => ChunkGenerationStatus.Biomes,
            >= StructureStarts and <= MaximumLoading =>
                ChunkGenerationStatus.StructureStarts,
            _ => ChunkGenerationStatus.Unloaded,
        };
}

public enum ChunkFullStatus : byte
{
    Inaccessible,
    Full,
    BlockTicking,
    EntityTicking,
}

public enum ChunkGenerationStatus : byte
{
    Unloaded,
    StructureStarts,
    StructureReferences,
    Biomes,
    Terrain,
    Features,
    InitializeLight,
    Light,
    Spawn,
    Full,
}

/// <summary>
/// A ticket is identified by its type and level on one horizontal chunk
/// column. Its flags determine whether it participates in loading,
/// simulation, or both.
/// </summary>
public readonly record struct ChunkTicket(
    ChunkTicketType Type,
    int Level)
{
    public ChunkTicketFlags Flags => Type switch
    {
        ChunkTicketType.PlayerLoading => ChunkTicketFlags.Loading,
        ChunkTicketType.PlayerSimulation =>
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.KeepDimensionActive,
        _ => throw new ArgumentOutOfRangeException(nameof(Type)),
    };
}

public readonly record struct ChunkTicketPlacement(
    ChunkColumnCoord Column,
    ChunkTicket Ticket);

/// <summary>
/// Authoritative set of ticket reasons per horizontal chunk column.
/// Re-adding an identical (type, level) ticket is a no-op, matching vanilla's
/// TicketStorage identity and replacement behavior.
/// </summary>
public sealed class ChunkTicketStorage
{
    private readonly Dictionary<ChunkColumnCoord, HashSet<ChunkTicket>> _tickets = [];

    public int ColumnCount => _tickets.Count;

    public int TicketCount => _tickets.Values.Sum(tickets => tickets.Count);

    public IEnumerable<(ChunkColumnCoord Column, ChunkTicket Ticket)> AllTickets =>
        _tickets.SelectMany(entry =>
            entry.Value.Select(ticket => (entry.Key, ticket)));

    public IReadOnlyCollection<ChunkTicket> GetTickets(ChunkColumnCoord column) =>
        _tickets.TryGetValue(column, out var tickets)
            ? tickets.ToArray()
            : Array.Empty<ChunkTicket>();

    public int GetLowestLevel(
        ChunkColumnCoord column,
        ChunkTicketFlags requiredFlags)
    {
        var lowest = int.MaxValue;
        if (!_tickets.TryGetValue(column, out var tickets))
            return lowest;

        foreach (var ticket in tickets)
        {
            if ((ticket.Flags & requiredFlags) == requiredFlags)
                lowest = Math.Min(lowest, ticket.Level);
        }

        return lowest;
    }

    public bool AddTicket(ChunkColumnCoord column, ChunkTicket ticket)
    {
        if (!Enum.IsDefined(ticket.Type))
            throw new ArgumentOutOfRangeException(nameof(ticket));
        if (ticket.Level < 0 || ticket.Level >= ChunkLevel.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(ticket));

        if (!_tickets.TryGetValue(column, out var tickets))
        {
            tickets = [];
            _tickets.Add(column, tickets);
        }

        return tickets.Add(ticket);
    }

    public bool RemoveTicket(ChunkColumnCoord column, ChunkTicket ticket)
    {
        if (!_tickets.TryGetValue(column, out var tickets) ||
            !tickets.Remove(ticket))
            return false;

        if (tickets.Count == 0)
            _tickets.Remove(column);
        return true;
    }

    public bool RemoveTicketsWhere(
        ChunkTicketType type,
        Predicate<ChunkColumnCoord> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        var removedAny = false;
        foreach (var (column, tickets) in _tickets.ToArray())
        {
            if (!predicate(column))
                continue;

            foreach (var ticket in tickets.Where(ticket => ticket.Type == type).ToArray())
                removedAny |= RemoveTicket(column, ticket);
        }

        return removedAny;
    }

    /// <summary>
    /// Replaces every ticket of one type with a new authoritative set.
    /// Other ticket types on the same columns are preserved.
    /// </summary>
    public bool ReplaceTickets(
        ChunkTicketType type,
        IEnumerable<ChunkTicketPlacement> replacements)
    {
        ArgumentNullException.ThrowIfNull(replacements);
        if (!Enum.IsDefined(type))
            throw new ArgumentOutOfRangeException(nameof(type));

        var desired = replacements
            .Where(item => item.Ticket.Type == type)
            .ToHashSet();
        var current = AllTickets
            .Where(item => item.Ticket.Type == type)
            .Select(item => new ChunkTicketPlacement(item.Column, item.Ticket))
            .ToArray();

        var changed = false;
        foreach (var existing in current)
        {
            if (!desired.Contains(existing))
                changed |= RemoveTicket(existing.Column, existing.Ticket);
        }

        foreach (var replacement in desired)
        {
            if (!AddTicket(replacement.Column, replacement.Ticket))
                continue;
            changed = true;
        }

        return changed;
    }
}

/// <summary>
/// Propagates the minimum ticket level outwards through the eight neighboring
/// columns. The priority queue is ordered by level then coordinates, so graph
/// results are deterministic regardless of dictionary enumeration order.
/// </summary>
public sealed class ChunkDistanceTracker
{
    private readonly Dictionary<ChunkColumnCoord, int> _levels = [];
    private readonly Dictionary<ChunkColumnCoord, int> _sources = [];

    public int Count => _levels.Count;

    public IReadOnlyDictionary<ChunkColumnCoord, int> Levels => _levels;

    public int MaximumLevel { get; private set; }

    public int GetLevel(ChunkColumnCoord column, int absentLevel) =>
        _levels.TryGetValue(column, out var level)
            ? level
            : absentLevel;

    public bool TryGetLevel(ChunkColumnCoord column, out int level) =>
        _levels.TryGetValue(column, out level);

    /// <summary>
    /// Initializes a distance graph from the current ticket sources.
    /// Subsequent ticket changes should use SynchronizeSources so only the
    /// affected fixed-point region is propagated.
    /// </summary>
    public void Rebuild(
        ChunkTicketStorage tickets,
        ChunkTicketFlags graphFlag,
        int maximumLevel)
    {
        _levels.Clear();
        _sources.Clear();
        MaximumLevel = maximumLevel;
        SynchronizeSources(tickets, graphFlag, maximumLevel);
    }

    /// <summary>
    /// Diffs ticket source levels, then propagates increases or decreases
    /// through the eight-neighbour graph until every changed node reaches its
    /// minimum fixed point: min(source level, neighbour level + 1).
    /// </summary>
    public void SynchronizeSources(
        ChunkTicketStorage tickets,
        ChunkTicketFlags graphFlag,
        int maximumLevel)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        if (graphFlag is not (ChunkTicketFlags.Loading or ChunkTicketFlags.Simulation))
            throw new ArgumentOutOfRangeException(nameof(graphFlag));
        if (maximumLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLevel));

        if (MaximumLevel != maximumLevel)
        {
            _levels.Clear();
            _sources.Clear();
            MaximumLevel = maximumLevel;
        }

        var desiredSources = new Dictionary<ChunkColumnCoord, int>();
        foreach (var (column, ticket) in tickets.AllTickets)
        {
            if ((ticket.Flags & graphFlag) != graphFlag ||
                ticket.Level > maximumLevel)
                continue;

            if (!desiredSources.TryGetValue(column, out var current) ||
                ticket.Level < current)
                desiredSources[column] = ticket.Level;
        }

        var pending = new Queue<ChunkColumnCoord>();
        var scheduled = new HashSet<ChunkColumnCoord>();

        void Schedule(ChunkColumnCoord column)
        {
            if (scheduled.Add(column))
                pending.Enqueue(column);
        }

        void ScheduleNeighbors(ChunkColumnCoord column)
        {
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                var x = (long)column.X + dx;
                var z = (long)column.Z + dz;
                if (x < int.MinValue || x > int.MaxValue ||
                    z < int.MinValue || z > int.MaxValue)
                    continue;

                Schedule(ChunkColumnCoord.FromChunk(
                    new ChunkCoord((int)x, 0, (int)z)));
            }
        }

        foreach (var column in _sources.Keys
                     .Concat(desiredSources.Keys)
                     .Distinct()
                     .ToArray())
        {
            var hadSource = _sources.TryGetValue(column, out var oldLevel);
            var hasSource = desiredSources.TryGetValue(column, out var newLevel);
            if (hadSource == hasSource &&
                (!hadSource || oldLevel == newLevel))
                continue;

            if (hasSource)
                _sources[column] = newLevel;
            else
                _sources.Remove(column);
            Schedule(column);
        }

        while (pending.TryDequeue(out var column))
        {
            scheduled.Remove(column);
            var nextLevel = _sources.TryGetValue(column, out var sourceLevel)
                ? sourceLevel
                : maximumLevel + 1;

            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                    continue;

                var x = (long)column.X + dx;
                var z = (long)column.Z + dz;
                if (x < int.MinValue || x > int.MaxValue ||
                    z < int.MinValue || z > int.MaxValue)
                    continue;

                var neighbor = ChunkColumnCoord.FromChunk(
                    new ChunkCoord((int)x, 0, (int)z));
                if (_levels.TryGetValue(neighbor, out var neighborLevel) &&
                    neighborLevel < maximumLevel)
                    nextLevel = Math.Min(nextLevel, neighborLevel + 1);
            }

            var hadLevel = _levels.TryGetValue(column, out var currentLevel);
            var boundedLevel = Math.Min(nextLevel, maximumLevel + 1);
            if (hadLevel && currentLevel == boundedLevel ||
                !hadLevel && boundedLevel > maximumLevel)
                continue;

            if (boundedLevel > maximumLevel)
                _levels.Remove(column);
            else
                _levels[column] = boundedLevel;

            // Both promotions and demotions can change a neighbour's best
            // path. Re-evaluate the surrounding ring until the graph settles.
            ScheduleNeighbors(column);
        }
    }
}

/// <summary>
/// One horizontal holder created by the loading graph. Its ticket level is a
/// ceiling on generation status, while its three futures represent actual
/// promotion to FULL, BLOCK_TICKING and ENTITY_TICKING.
/// </summary>
public sealed class ChunkHolder
{
    private TaskCompletionSource<ChunkHolderFutureResult> _fullChunkFuture = NewFuture();
    private TaskCompletionSource<ChunkHolderFutureResult> _blockTickingFuture = NewFuture();
    private TaskCompletionSource<ChunkHolderFutureResult> _entityTickingFuture = NewFuture();

    internal ChunkHolder(ChunkColumnCoord position, int ticketLevel)
    {
        Position = position;
        UpdateTicketLevel(ticketLevel);
    }

    public ChunkColumnCoord Position { get; }

    public int TicketLevel { get; private set; }

    public ChunkGenerationStatus HighestAllowedGenerationStatus { get; private set; }

    /// <summary>Maximum full-chunk status permitted by the loading ticket level.</summary>
    public ChunkFullStatus FullStatus { get; private set; }

    public ChunkFullStatus CompletedFullStatus
    {
        get
        {
            if (Succeeded(_entityTickingFuture))
                return ChunkFullStatus.EntityTicking;
            if (Succeeded(_blockTickingFuture))
                return ChunkFullStatus.BlockTicking;
            if (Succeeded(_fullChunkFuture))
                return ChunkFullStatus.Full;
            return ChunkFullStatus.Inaccessible;
        }
    }

    public Task<ChunkHolderFutureResult> FullChunkFuture =>
        _fullChunkFuture.Task;

    public Task<ChunkHolderFutureResult> BlockTickingFuture =>
        _blockTickingFuture.Task;

    public Task<ChunkHolderFutureResult> EntityTickingFuture =>
        _entityTickingFuture.Task;

    public bool IsLoading => TicketLevel < ChunkLevel.Unloaded;

    public ulong TicketLevelRevision { get; private set; }

    internal bool UpdateTicketLevel(int ticketLevel)
    {
        if (ticketLevel < 0 || ticketLevel > ChunkLevel.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(ticketLevel));
        if (TicketLevel == ticketLevel && TicketLevelRevision != 0)
            return false;

        TicketLevel = ticketLevel;
        HighestAllowedGenerationStatus =
            ChunkLevel.GetGenerationStatus(ticketLevel);
        FullStatus = ChunkLevel.GetFullStatus(ticketLevel);
        TicketLevelRevision++;

        ReconcileFuture(
            ref _fullChunkFuture,
            FullStatus >= ChunkFullStatus.Full,
            "Chunk is no longer allowed to remain FULL.");
        ReconcileFuture(
            ref _blockTickingFuture,
            FullStatus >= ChunkFullStatus.BlockTicking,
            "Chunk is no longer allowed to tick blocks.");
        ReconcileFuture(
            ref _entityTickingFuture,
            FullStatus >= ChunkFullStatus.EntityTicking,
            "Chunk is no longer allowed to tick entities.");
        return true;
    }

    /// <summary>
    /// Completes every promotion future covered by the supplied achieved
    /// status. A future is never completed successfully above the current
    /// loading-ticket ceiling.
    /// </summary>
    public bool CompleteFullStatus(ChunkFullStatus completedStatus)
    {
        if (completedStatus <= ChunkFullStatus.Inaccessible)
            throw new ArgumentOutOfRangeException(nameof(completedStatus));
        if (completedStatus > FullStatus)
            return false;

        var changed = false;
        if (completedStatus >= ChunkFullStatus.Full)
            changed |= _fullChunkFuture.TrySetResult(
                ChunkHolderFutureResult.Success(ChunkFullStatus.Full));
        if (completedStatus >= ChunkFullStatus.BlockTicking)
            changed |= _blockTickingFuture.TrySetResult(
                ChunkHolderFutureResult.Success(ChunkFullStatus.BlockTicking));
        if (completedStatus >= ChunkFullStatus.EntityTicking)
            changed |= _entityTickingFuture.TrySetResult(
                ChunkHolderFutureResult.Success(ChunkFullStatus.EntityTicking));
        return changed;
    }

    /// <summary>
    /// Completes pending promotion futures with a failed ChunkResult. The
    /// ticket stays installed, so a later retry may arm fresh futures.
    /// </summary>
    public bool FailPendingPromotions(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        var failure = ChunkHolderFutureResult.Unloaded(reason);
        var changed = false;
        if (FullStatus >= ChunkFullStatus.Full)
            changed |= _fullChunkFuture.TrySetResult(failure);
        if (FullStatus >= ChunkFullStatus.BlockTicking)
            changed |= _blockTickingFuture.TrySetResult(failure);
        if (FullStatus >= ChunkFullStatus.EntityTicking)
            changed |= _entityTickingFuture.TrySetResult(failure);
        return changed;
    }

    private static TaskCompletionSource<ChunkHolderFutureResult> NewFuture() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool Succeeded(
        TaskCompletionSource<ChunkHolderFutureResult> future) =>
        future.Task.IsCompletedSuccessfully &&
        future.Task.Result.Succeeded;

    private static void ReconcileFuture(
        ref TaskCompletionSource<ChunkHolderFutureResult> future,
        bool allowed,
        string demotionReason)
    {
        if (allowed)
        {
            if (future.Task.IsCompletedSuccessfully &&
                !future.Task.Result.Succeeded)
                future = NewFuture();
            return;
        }

        if (future.Task.IsCompletedSuccessfully)
        {
            if (future.Task.Result.Succeeded)
            {
                future = NewFuture();
                future.TrySetResult(
                    ChunkHolderFutureResult.Unloaded(demotionReason));
            }
            return;
        }

        future.TrySetResult(
            ChunkHolderFutureResult.Unloaded(demotionReason));
    }
}

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
    private readonly Dictionary<ChunkColumnCoord, ChunkHolder> _holders = [];

    public ChunkTicketStorage Tickets => _tickets;

    public ChunkDistanceTracker LoadingTracker => _loading;

    public ChunkDistanceTracker SimulationTracker => _simulation;

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
        if (!loadingChanged && !simulationChanged)
            return false;

        RebuildDistanceGraphs(loadingChanged, simulationChanged);
        return true;
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

    private void RebuildDistanceGraphs(
        bool loadingChanged,
        bool simulationChanged)
    {
        if (loadingChanged)
        {
            _loading.Rebuild(
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
            if (_loading.Levels.ContainsKey(column))
                continue;

            // A holder leaving the loading graph is demoted to UNLOADED
            // before it is removed from the visible holder map. Existing
            // future observers must receive the demotion, not wait forever.
            holder.UpdateTicketLevel(ChunkLevel.Unloaded);
            _holders.Remove(column);
        }
    }

    public bool CompleteFullStatus(
        ChunkColumnCoord column,
        ChunkFullStatus status) =>
        _holders.TryGetValue(column, out var holder) &&
        holder.CompleteFullStatus(status);

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
