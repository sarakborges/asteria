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
    // Only UNKNOWN uses this: its countdown runs even if its holder is still loading.
    CanExpireIfUnloaded = 1 << 4,
}

/// <summary>
/// Sources of chunk interest. Flags choose the loading/simulation graphs,
/// persistence and the special UNKNOWN expiry behavior. Other timed tickets
/// pause their countdown until the holder is absent or ready for saving.
/// </summary>
public enum ChunkTicketType : byte
{
    PlayerLoading,
    PlayerSimulation,
    Forced,
    Portal,
    EnderPearl,
    Dragon,
    PlayerSpawn,
    SpawnSearch,
    Unknown,
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

    // The loading graph extends only as far as Asteria's transitive holder-
    // status dependencies require. Immutable generator-field read halos do not
    // create or retain chunk holders. This is derived from the stage DAG, not
    // copied from Minecraft's eleven-ring vanilla pyramid.
    public static int MaximumLoading =>
        Full + AsteriaChunkGenerationPlan.MaximumHorizontalDependencyRadius;
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

    public static ChunkGenerationStatus GetGenerationStatus(int level)
    {
        if (level <= Full)
            return ChunkGenerationStatus.Full;

        var horizontalDistance = level - Full;
        if (horizontalDistance > AsteriaChunkGenerationPlan.MaximumHorizontalDependencyRadius)
            return ChunkGenerationStatus.Unloaded;

        return AsteriaChunkGenerationPlan.GetRequiredStatusAtHorizontalDistance(
            ChunkGenerationStepId.SectionReady,
            horizontalDistance);
    }
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
    int Level,
    ulong Key = 0)
{
    public ChunkTicketFlags Flags => Type switch
    {
        ChunkTicketType.PlayerLoading =>
            ChunkTicketFlags.Loading,
        ChunkTicketType.PlayerSimulation =>
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.KeepDimensionActive,
        ChunkTicketType.Forced =>
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.Persist |
            ChunkTicketFlags.KeepDimensionActive,
        ChunkTicketType.Portal =>
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.Persist |
            ChunkTicketFlags.KeepDimensionActive,
        ChunkTicketType.EnderPearl =>
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation |
            ChunkTicketFlags.KeepDimensionActive,
        ChunkTicketType.Dragon =>
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.Simulation,
        ChunkTicketType.PlayerSpawn or
        ChunkTicketType.SpawnSearch =>
            ChunkTicketFlags.Loading,
        ChunkTicketType.Unknown =>
            ChunkTicketFlags.Loading |
            ChunkTicketFlags.CanExpireIfUnloaded,
        _ => throw new ArgumentOutOfRangeException(nameof(Type)),
    };

    public ulong? TimeoutTicks => Type switch
    {
        ChunkTicketType.Portal => 300,
        ChunkTicketType.EnderPearl => 40,
        ChunkTicketType.PlayerSpawn => 20,
        ChunkTicketType.SpawnSearch or ChunkTicketType.Unknown => 1,
        _ => null,
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
    private readonly Dictionary<(ChunkColumnCoord Column, ChunkTicket Ticket), TicketExpiration>
        _expirationTicks = [];

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

    public bool AddTicket(
        ChunkColumnCoord column,
        ChunkTicket ticket,
        ulong currentTick = 0)
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

        var added = tickets.Add(ticket);
        if (ticket.TimeoutTicks is { } timeout)
        {
            var expiration = new TicketExpiration(
                SaturatingAdd(currentTick, timeout), currentTick);
            var key = (column, ticket);
            if (!_expirationTicks.TryGetValue(key, out var existing) ||
                expiration != existing)
            {
                // Re-adding the same ticket resets its countdown just as
                // Ticket.resetTicksLeft does in Minecraft.
                _expirationTicks[key] = expiration;
                added = true;
            }
        }

        return added;
    }

    /// <summary>
    /// Advances timed ticket countdowns. Non-UNKNOWN tickets pause while their
    /// holder is neither absent nor ready for saving. Other identities in the
    /// same column remain untouched. The returned flags tell DistanceManager
    /// which distance graphs need to be propagated after an expiry.
    /// </summary>
    public ChunkTicketFlags ExpireTickets(
        ulong currentTick,
        Func<ChunkColumnCoord, bool> isHolderReadyForSaving)
    {
        ArgumentNullException.ThrowIfNull(isHolderReadyForSaving);
        var changed = ChunkTicketFlags.None;
        foreach (var (identity, expiration) in _expirationTicks.ToArray())
        {
            if (currentTick < expiration.LastEvaluatedTick)
                throw new InvalidOperationException(
                    "Chunk ticket expiration ticks must be monotonic.");

            var elapsed = currentTick - expiration.LastEvaluatedTick;
            var mayExpireWhileLoaded =
                (identity.Ticket.Flags & ChunkTicketFlags.CanExpireIfUnloaded) != 0;
            var countdownRuns = mayExpireWhileLoaded ||
                isHolderReadyForSaving(identity.Column);

            if (!countdownRuns)
            {
                // Non-UNKNOWN timed tickets pause while a holder is still
                // being generated or retained by another generation task.
                _expirationTicks[identity] = new TicketExpiration(
                    SaturatingAdd(expiration.ExpiresAtTick, elapsed), currentTick);
                continue;
            }

            if (currentTick < expiration.ExpiresAtTick)
            {
                _expirationTicks[identity] = expiration with
                {
                    LastEvaluatedTick = currentTick,
                };
                continue;
            }

            if (RemoveTicket(identity.Column, identity.Ticket))
                changed |= identity.Ticket.Flags &
                    (ChunkTicketFlags.Loading | ChunkTicketFlags.Simulation);
        }

        return changed;
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private readonly record struct TicketExpiration(
        ulong ExpiresAtTick,
        ulong LastEvaluatedTick);

    public bool RemoveTicket(ChunkColumnCoord column, ChunkTicket ticket)
    {
        if (!_tickets.TryGetValue(column, out var tickets) ||
            !tickets.Remove(ticket))
            return false;

        _expirationTicks.Remove((column, ticket));
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
        IEnumerable<ChunkTicketPlacement> replacements,
        ulong currentTick = 0)
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
            if (!AddTicket(replacement.Column, replacement.Ticket, currentTick))
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
/// <summary>
/// Minecraft's naturalSpawnChunkCounter is a third, fixed-radius tracker,
/// independent of both ticket distance graphs. It selects candidate columns;
/// the entity-ticking graph remains a separate gate before spawning.
/// </summary>
public sealed class NaturalSpawnChunkTracker
{
    public const int RadiusChunks = 8;

    private readonly HashSet<ChunkColumnCoord> _columns = [];
    public ChunkColumnCoord? Center { get; private set; }

    public int Count => _columns.Count;

    public bool Contains(ChunkColumnCoord column) =>
        _columns.Contains(column);

    public bool UpdateCenter(ChunkColumnCoord center)
    {
        if (Center == center)
            return false;

        _columns.Clear();
        for (var dz = -RadiusChunks; dz <= RadiusChunks; dz++)
        for (var dx = -RadiusChunks; dx <= RadiusChunks; dx++)
        {
            var x = (long)center.X + dx;
            var z = (long)center.Z + dz;
            if (x < int.MinValue || x > int.MaxValue ||
                z < int.MinValue || z > int.MaxValue)
                continue;

            _columns.Add(ChunkColumnCoord.FromChunk(
                new ChunkCoord((int)x, 0, (int)z)));
        }

        Center = center;
        return true;
    }
}

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

    /// <summary>Initializes a graph from the current ticket source set.</summary>
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
    /// Diffs ticket sources and recomputes only the bounded region whose
    /// shortest paths could be affected by those sources. The unaffected
    /// boundary remains a set of valid seeds. A priority flood then restores
    /// the exact minimum fixed point inside the affected region.
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

        var changedSources = new List<ChunkColumnCoord>();
        foreach (var column in _sources.Keys
                     .Concat(desiredSources.Keys)
                     .Distinct())
        {
            var hadSource = _sources.TryGetValue(column, out var oldLevel);
            var hasSource = desiredSources.TryGetValue(column, out var newLevel);
            if (hadSource == hasSource &&
                (!hadSource || oldLevel == newLevel))
                continue;

            changedSources.Add(column);
        }

        if (changedSources.Count == 0)
            return;

        // A source at level L can influence at most (maximumLevel - L)
        // neighbour rings. Include that entire Chebyshev region so no stale
        // level derived from a removed source can become a boundary seed.
        var affected = new HashSet<ChunkColumnCoord>();
        foreach (var column in changedSources)
        {
            var hadSource = _sources.TryGetValue(column, out var oldLevel);
            var hasSource = desiredSources.TryGetValue(column, out var newLevel);
            var sourceLevel = hadSource && hasSource
                ? Math.Min(oldLevel, newLevel)
                : hadSource ? oldLevel : newLevel;
            var radius = maximumLevel - sourceLevel;

            for (var dz = -radius; dz <= radius; dz++)
            for (var dx = -radius; dx <= radius; dx++)
            {
                var x = (long)column.X + dx;
                var z = (long)column.Z + dz;
                if (x < int.MinValue || x > int.MaxValue ||
                    z < int.MinValue || z > int.MaxValue)
                    continue;

                affected.Add(ChunkColumnCoord.FromChunk(
                    new ChunkCoord((int)x, 0, (int)z)));
            }
        }

        _sources.Clear();
        foreach (var (column, level) in desiredSources)
            _sources.Add(column, level);

        foreach (var column in affected)
            _levels.Remove(column);

        var pending = new PriorityQueue<
            ChunkColumnCoord, (int Level, int Z, int X)>();

        // Every affected node can be reached either from a source inside the
        // region or from an unchanged level just outside its boundary.
        foreach (var column in affected)
        {
            var best = _sources.TryGetValue(column, out var sourceLevel)
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
                if (affected.Contains(neighbor) ||
                    !_levels.TryGetValue(neighbor, out var neighborLevel) ||
                    neighborLevel >= maximumLevel)
                    continue;

                best = Math.Min(best, neighborLevel + 1);
            }

            if (best > maximumLevel)
                continue;

            _levels[column] = best;
            pending.Enqueue(column, (best, column.Z, column.X));
        }

        while (pending.TryDequeue(out var column, out var priority))
        {
            if (!_levels.TryGetValue(column, out var currentLevel) ||
                currentLevel != priority.Level ||
                currentLevel >= maximumLevel)
                continue;

            var nextLevel = currentLevel + 1;
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
                if (!affected.Contains(neighbor))
                    continue;

                var candidate = _sources.TryGetValue(
                    neighbor, out var neighborSource)
                    ? Math.Min(nextLevel, neighborSource)
                    : nextLevel;

                if (candidate > maximumLevel ||
                    _levels.TryGetValue(neighbor, out var existingLevel) &&
                    existingLevel <= candidate)
                    continue;

                _levels[neighbor] = candidate;
                pending.Enqueue(
                    neighbor,
                    (candidate, neighbor.Z, neighbor.X));
            }
        }
    }
}

/// <summary>
/// Result delivered by one of a chunk holder's asynchronous status futures.
/// Failed promotions carry a reason and are completed as values, allowing
/// callers to observe demotion without faulted continuation chains.
/// </summary>
public readonly record struct ChunkHolderFutureResult(
    bool Succeeded,
    ChunkFullStatus Status,
    string? FailureReason)
{
    public static ChunkHolderFutureResult Success(ChunkFullStatus status) =>
        new(true, status, null);

    public static ChunkHolderFutureResult Unloaded(string reason) =>
        new(false, ChunkFullStatus.Inaccessible, reason);
}

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
/// One horizontal holder created by the loading graph. Its ticket level is a
/// ceiling on generation status, while section-local futures retain exact
/// three-dimensional progress within the same X/Z holder.
/// </summary>
public sealed class ChunkHolder
{
    private TaskCompletionSource<ChunkHolderFutureResult> _fullChunkFuture = NewFuture();
    private TaskCompletionSource<ChunkHolderFutureResult> _blockTickingFuture = NewFuture();
    private TaskCompletionSource<ChunkHolderFutureResult> _entityTickingFuture = NewFuture();
    private readonly Dictionary<ChunkGenerationStatus,
        TaskCompletionSource<ChunkGenerationFutureResult>> _generationFutures =
        CreateGenerationFutures();
    private readonly Dictionary<ChunkCoord, ChunkSectionGenerationProgressState>
        _sectionGenerationProgress = [];
    private int _dependencyClaimCount;

    internal ChunkHolder(ChunkColumnCoord position, int ticketLevel)
    {
        Position = position;
        UpdateTicketLevel(ticketLevel);
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

    /// <summary>
    /// A timed ticket may count down while its holder exists only after FULL
    /// promotion has completed and no generation task retains the holder.
    /// </summary>
    public bool IsReadyForSaving =>
        DependencyClaimCount == 0 &&
        CompletedFullStatus >= ChunkFullStatus.Full;

    public Task<ChunkHolderFutureResult> FullChunkFuture =>
        _fullChunkFuture.Task;

    public Task<ChunkHolderFutureResult> BlockTickingFuture =>
        _blockTickingFuture.Task;

    public Task<ChunkHolderFutureResult> EntityTickingFuture =>
        _entityTickingFuture.Task;

    public bool IsLoading => TicketLevel < ChunkLevel.Unloaded;

    /// <summary>Generation tasks retaining this holder as a dependency.</summary>
    public int DependencyClaimCount => Volatile.Read(ref _dependencyClaimCount);

    public ulong TicketLevelRevision { get; private set; }

    internal void AddDependencyClaim() =>
        Interlocked.Increment(ref _dependencyClaimCount);

    internal void RemoveDependencyClaim()
    {
        if (Interlocked.Decrement(ref _dependencyClaimCount) < 0)
            throw new InvalidOperationException(
                $"Chunk holder dependency claim count underflowed: {Position}");
    }

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
        if (!Enum.IsDefined(requestedStatus) ||
            requestedStatus == ChunkGenerationStatus.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(requestedStatus));

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
        if (!Enum.IsDefined(requestedStatus) ||
            requestedStatus == ChunkGenerationStatus.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(requestedStatus));

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
        return GetOrCreateSectionProgress(section).Complete(
            completedStatus, HighestAllowedGenerationStatus, TicketLevel, product);
    }

    public bool ResetSectionGenerationProgress(
        ChunkCoord section,
        string reason)
    {
        ValidateSection(section);
        if (!_sectionGenerationProgress.TryGetValue(section, out var progress) ||
            !progress.HasProgressOrCompletedFuture)
            return false;

        // Reset rearms every section future in an incomplete state. A future
        // request above the current ticket ceiling is rejected by GetFuture;
        // the next ticket change reconciles the cached futures.
        progress.Reset(reason);
        return true;
    }

    /// <summary>
    /// Completes a generation status and every earlier status future. This
    /// reports actual achieved progress; it does not change the ticket ceiling.
    /// </summary>
    public bool CompleteGenerationStatus(ChunkGenerationStatus completedStatus)
    {
        if (!Enum.IsDefined(completedStatus) ||
            completedStatus == ChunkGenerationStatus.Unloaded)
            throw new ArgumentOutOfRangeException(nameof(completedStatus));

        // Achieved progress is independent from the current ticket ceiling.
        // When tickets later promote this holder, ReconcileGenerationFutures
        // completes the newly permitted futures from the retained progress.
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
    /// Invalidates partial products discarded after a failed/cancelled
    /// generation attempt. Existing waiters are released with a failed result;
    /// future requests receive fresh tasks under the new progress revision.
    /// </summary>
    public bool ResetGenerationProgress(string reason)
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

        ResetPromotionFuture(ref _fullChunkFuture, reason);
        ResetPromotionFuture(ref _blockTickingFuture, reason);
        ResetPromotionFuture(ref _entityTickingFuture, reason);

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
        if (ChunkColumnCoord.FromChunk(section) != Position)
            throw new ArgumentException(
                $"Section {section} does not belong to holder {Position}.",
                nameof(section));
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

    private static TaskCompletionSource<ChunkHolderFutureResult> NewFuture() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private static bool Succeeded(
        TaskCompletionSource<ChunkHolderFutureResult> future) =>
        future.Task.IsCompletedSuccessfully &&
        future.Task.Result.Succeeded;

    private static void ResetPromotionFuture(
        ref TaskCompletionSource<ChunkHolderFutureResult> future,
        string reason)
    {
        future.TrySetResult(ChunkHolderFutureResult.Unloaded(reason));
        future = NewFuture();
    }

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
/// Reference-counted lease retaining the holder set required by one generation
/// task. Disposing the lease releases those references exactly once.
/// </summary>
public sealed class ChunkDependencyClaim : IDisposable
{
    private ChunkDistanceManager? _owner;
    private readonly ChunkColumnCoord[] _columns;
    private readonly ChunkCoord[] _sectionTargets;

    internal ChunkDependencyClaim(
        ChunkDistanceManager owner,
        ChunkColumnCoord[] columns,
        ChunkCoord[] sectionTargets)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        _columns = columns ?? throw new ArgumentNullException(nameof(columns));
        _sectionTargets = sectionTargets ?? throw new ArgumentNullException(nameof(sectionTargets));
    }

    public IReadOnlyList<ChunkColumnCoord> Columns => _columns;

    /// <summary>Exact 3D section coordinates covered by the generation dependency envelope.</summary>
    public IReadOnlyList<ChunkCoord> SectionTargets => _sectionTargets;

    /// <summary>
    /// Requests the holder-owned future for an exact section/status retained by
    /// this lease. The horizontal holder is shared across its vertical sections.
    /// </summary>
    public Task<ChunkGenerationFutureResult> GetSectionGenerationFuture(
        ChunkCoord section,
        ChunkGenerationStatus status)
    {
        if (!_sectionTargets.Contains(section))
            throw new ArgumentException(
                $"Section {section} is outside this dependency claim.",
                nameof(section));
        var owner = Volatile.Read(ref _owner) ??
            throw new ObjectDisposedException(nameof(ChunkDependencyClaim));
        return owner.GetSectionGenerationFuture(section, status);
    }

    public Task<ChunkGenerationFutureResult> GetSectionGenerationFuture(
        ChunkGenerationSectionRequirement requirement)
    {
        var owner = Volatile.Read(ref _owner) ??
            throw new ObjectDisposedException(nameof(ChunkDependencyClaim));
        if (!_sectionTargets.Contains(requirement.Coord))
            throw new ArgumentException(
                $"Section {requirement.Coord} is outside this dependency claim.",
                nameof(requirement));
        return owner.GetSectionGenerationFuture(
            requirement.Coord, requirement.Status);
    }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        owner?.ReleaseDependencyClaims(_columns);
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
                    if (sectionY < int.MinValue || sectionY > int.MaxValue)
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
        ValidateGenerationStatus(requestedStatus);
        var column = ChunkColumnCoord.FromChunk(section);
        return _holders.TryGetValue(column, out var holder)
            ? holder.GetSectionGenerationFuture(section, requestedStatus)
            : Task.FromResult(ChunkGenerationFutureResult.Unloaded(
                requestedStatus,
                $"Chunk holder does not exist for section {section}."));
    }

    public ChunkGenerationStatus GetCompletedSectionGenerationStatus(
        ChunkCoord section) =>
        _holders.TryGetValue(
            ChunkColumnCoord.FromChunk(section), out var holder)
            ? holder.GetCompletedSectionGenerationStatus(section)
            : ChunkGenerationStatus.Unloaded;

    public ulong GetSectionGenerationRevision(ChunkCoord section) =>
        _holders.TryGetValue(
            ChunkColumnCoord.FromChunk(section), out var holder)
            ? holder.GetSectionGenerationRevision(section)
            : 0UL;

    public bool CompleteSectionGenerationStatus(
        ChunkCoord section,
        ChunkGenerationStatus status,
        Chunk? product = null) =>
        _holders.TryGetValue(
            ChunkColumnCoord.FromChunk(section), out var holder) &&
        holder.CompleteSectionGenerationStatus(section, status, product);

    public bool ResetSectionGenerationProgress(
        ChunkCoord section,
        string reason) =>
        _holders.TryGetValue(
            ChunkColumnCoord.FromChunk(section), out var holder) &&
        holder.ResetSectionGenerationProgress(section, reason);

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
