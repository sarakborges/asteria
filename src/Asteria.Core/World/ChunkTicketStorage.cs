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

    public int Count => _levels.Count;

    public int MaximumLevel { get; private set; }

    public int GetLevel(ChunkColumnCoord column, int absentLevel) =>
        _levels.TryGetValue(column, out var level)
            ? level
            : absentLevel;

    public bool TryGetLevel(ChunkColumnCoord column, out int level) =>
        _levels.TryGetValue(column, out level);

    public void Rebuild(
        ChunkTicketStorage tickets,
        ChunkTicketFlags graphFlag,
        int maximumLevel)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        if (graphFlag is not (ChunkTicketFlags.Loading or ChunkTicketFlags.Simulation))
            throw new ArgumentOutOfRangeException(nameof(graphFlag));
        if (maximumLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLevel));

        _levels.Clear();
        MaximumLevel = maximumLevel;
        var pending = new PriorityQueue<
            ChunkColumnCoord, (int Level, int Z, int X)>();

        foreach (var (column, ticket) in tickets.AllTickets)
        {
            if ((ticket.Flags & graphFlag) != graphFlag ||
                ticket.Level > maximumLevel)
                continue;

            AddOrLower(column, ticket.Level, pending);
        }

        while (pending.TryDequeue(out var column, out var priority))
        {
            if (!_levels.TryGetValue(column, out var currentLevel) ||
                currentLevel != priority.Level)
                continue;

            if (currentLevel >= maximumLevel)
                continue;

            var neighborLevel = currentLevel + 1;
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
                AddOrLower(neighbor, neighborLevel, pending);
            }
        }
    }

    private void AddOrLower(
        ChunkColumnCoord column,
        int level,
        PriorityQueue<ChunkColumnCoord, (int Level, int Z, int X)> pending)
    {
        if (_levels.TryGetValue(column, out var current) &&
            current <= level)
            return;

        _levels[column] = level;
        pending.Enqueue(column, (level, column.Z, column.X));
    }
}

/// <summary>
/// Owns the ticket store and its independent loading and simulation distance
/// graphs. Player view tickets decide which columns may generate; the player
/// simulation ticket decides which loaded columns are allowed to tick.
/// </summary>
public sealed class ChunkDistanceManager
{
    private readonly ChunkTicketStorage _tickets = new();
    private readonly ChunkDistanceTracker _loading = new();
    private readonly ChunkDistanceTracker _simulation = new();

    public ChunkTicketStorage Tickets => _tickets;

    public ChunkDistanceTracker LoadingTracker => _loading;

    public ChunkDistanceTracker SimulationTracker => _simulation;

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

        _loading.Rebuild(
            _tickets,
            ChunkTicketFlags.Loading,
            ChunkLevel.MaximumLoading);
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

        _loading.Rebuild(
            _tickets,
            ChunkTicketFlags.Loading,
            ChunkLevel.MaximumLoading);
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
        }

        if (simulationChanged)
        {
            _simulation.Rebuild(
                _tickets,
                ChunkTicketFlags.Simulation,
                ChunkLevel.BlockTicking);
        }
    }

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
