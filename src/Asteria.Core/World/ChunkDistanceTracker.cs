namespace Asteria.Core.World;

public readonly record struct ChunkDistanceLevelChange(
    ChunkColumnCoord Column,
    int? PreviousLevel,
    int? Level);

public readonly record struct ChunkDistanceUpdateBatch(
    int ProcessedUpdates,
    IReadOnlyList<ChunkDistanceLevelChange> Changes);

/// <summary>
/// Propagates the minimum ticket level outwards through the eight neighboring
/// columns. The priority queue is ordered by level then coordinates, so graph
/// results are deterministic regardless of dictionary enumeration order.
/// </summary>
public sealed class ChunkDistanceTracker
{
    private static readonly IComparer<ChunkColumnCoord> ColumnOrder =
        Comparer<ChunkColumnCoord>.Create((left, right) =>
        {
            var result = left.X.CompareTo(right.X);
            return result != 0 ? result : left.Z.CompareTo(right.Z);
        });

    private readonly Dictionary<ChunkColumnCoord, int> _levels = [];
    private readonly Dictionary<ChunkColumnCoord, int> _sources = [];
    private readonly Dictionary<ChunkColumnCoord, PendingDistanceUpdate>
        _pendingUpdates = [];
    private SortedSet<ChunkColumnCoord>[] _pendingByLevel = [];
    private int _firstPendingLevel;

    public int Count => _levels.Count;

    public int PendingUpdateCount => _pendingUpdates.Count;

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
        _pendingUpdates.Clear();
        MaximumLevel = maximumLevel;
        ConfigurePendingQueue(maximumLevel);
        SynchronizeSources(tickets, graphFlag, maximumLevel);
    }

    /// <summary>
    /// Full-source synchronization for initialization, restoration and
    /// explicit graph rebuilds. Normal ticket mutations use ApplySourceChanges.
    /// </summary>
    public void SynchronizeSources(
        ChunkTicketStorage tickets,
        ChunkTicketFlags graphFlag,
        int maximumLevel) =>
        SynchronizeSourcesCore(tickets, graphFlag, maximumLevel, captureChanges: false);

    /// <summary>
    /// Full-source synchronization that also returns the resulting graph delta.
    /// </summary>
    public IReadOnlyList<ChunkDistanceLevelChange> SynchronizeSourcesWithChanges(
        ChunkTicketStorage tickets,
        ChunkTicketFlags graphFlag,
        int maximumLevel) =>
        SynchronizeSourcesCore(tickets, graphFlag, maximumLevel, captureChanges: true);

    /// <summary>
    /// Applies only ticket-source changes received from TicketStorage. This is
    /// the normal mutation path; its work is bounded by the changed sources and
    /// the graph regions those sources can influence.
    /// </summary>
    public IReadOnlyList<ChunkDistanceLevelChange> ApplySourceChanges(
        IEnumerable<KeyValuePair<ChunkColumnCoord, int>> changedSources,
        int maximumLevel)
    {
        EnqueueSourceChanges(changedSources, maximumLevel);
        return RunUpdates(int.MaxValue).Changes;
    }

    /// <summary>
    /// Updates the authoritative ticket-source set and queues every affected
    /// node without propagating the graph. The caller controls when, and how
    /// many, queued nodes are processed through RunUpdates.
    /// </summary>
    public void EnqueueSourceChanges(
        IEnumerable<KeyValuePair<ChunkColumnCoord, int>> changedSources,
        int maximumLevel)
    {
        ArgumentNullException.ThrowIfNull(changedSources);
        if (maximumLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLevel));

        if (MaximumLevel != maximumLevel)
        {
            if (_levels.Count != 0 || _sources.Count != 0)
                throw new InvalidOperationException(
                    "Changing the graph ceiling requires a full source synchronization.");

            MaximumLevel = maximumLevel;
        }
        ConfigurePendingQueue(maximumLevel);

        var sourceChanges = new Dictionary<ChunkColumnCoord, int?>();
        foreach (var (column, level) in changedSources)
        {
            if (level < 0)
                throw new ArgumentOutOfRangeException(nameof(changedSources));
            sourceChanges[column] = level == int.MaxValue ? null : level;
        }

        QueueSourceChangesCore(sourceChanges, maximumLevel);
    }

    /// <summary>
    /// Processes at most maximumUpdates nodes from the leveled queue. Any
    /// remaining work stays queued for a later world tick/frame.
    /// </summary>
    public ChunkDistanceUpdateBatch RunUpdates(int maximumUpdates)
    {
        if (maximumUpdates < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumUpdates));

        return RunPendingUpdates(maximumUpdates);
    }

    private IReadOnlyList<ChunkDistanceLevelChange> SynchronizeSourcesCore(
        ChunkTicketStorage tickets,
        ChunkTicketFlags graphFlag,
        int maximumLevel,
        bool captureChanges)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        if (graphFlag is not (ChunkTicketFlags.Loading or ChunkTicketFlags.Simulation))
            throw new ArgumentOutOfRangeException(nameof(graphFlag));
        if (maximumLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumLevel));

        Dictionary<ChunkColumnCoord, int>? previousFullGraph = null;
        if (MaximumLevel != maximumLevel)
        {
            if (captureChanges)
                previousFullGraph = new Dictionary<ChunkColumnCoord, int>(_levels);
            _levels.Clear();
            _sources.Clear();
            _pendingUpdates.Clear();
            MaximumLevel = maximumLevel;
        }
        ConfigurePendingQueue(maximumLevel);

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

        var sourceChanges = new Dictionary<ChunkColumnCoord, int?>();
        foreach (var column in _sources.Keys.Concat(desiredSources.Keys).Distinct())
        {
            var hadSource = _sources.TryGetValue(column, out var oldLevel);
            var hasSource = desiredSources.TryGetValue(column, out var newLevel);
            if (hadSource == hasSource &&
                (!hadSource || oldLevel == newLevel))
                continue;

            sourceChanges[column] = hasSource ? newLevel : null;
        }

        QueueSourceChangesCore(sourceChanges, maximumLevel);
        var batch = RunUpdates(int.MaxValue);

        if (previousFullGraph is not null)
            return CreateChanges(previousFullGraph, _levels);
        return captureChanges
            ? batch.Changes
            : Array.Empty<ChunkDistanceLevelChange>();
    }

    private void QueueSourceChangesCore(
        IReadOnlyDictionary<ChunkColumnCoord, int?> sourceChanges,
        int maximumLevel)
    {
        foreach (var (column, proposedLevel) in sourceChanges
                     .OrderBy(entry => entry.Key.X)
                     .ThenBy(entry => entry.Key.Z))
        {
            var hadSource = _sources.TryGetValue(column, out var oldLevel);
            var hasSource = proposedLevel is { } nextLevel &&
                nextLevel <= maximumLevel;
            var newLevel = hasSource ? proposedLevel.GetValueOrDefault() : 0;
            if (hadSource == hasSource &&
                (!hadSource || oldLevel == newLevel))
                continue;

            if (hasSource)
                _sources[column] = newLevel;
            else
                _sources.Remove(column);

            EnqueueIfChanged(column);
        }
    }

    private void ConfigurePendingQueue(int maximumLevel)
    {
        if (maximumLevel == int.MaxValue)
            throw new ArgumentOutOfRangeException(
                nameof(maximumLevel), "The graph ceiling must leave room for the unloaded sentinel.");

        if (_pendingByLevel.Length == maximumLevel + 1)
            return;
        if (_pendingUpdates.Count != 0)
            throw new InvalidOperationException(
                "Cannot resize the distance queue while graph updates are pending.");

        _pendingByLevel = Enumerable.Range(0, maximumLevel + 1)
            .Select(_ => new SortedSet<ChunkColumnCoord>(ColumnOrder))
            .ToArray();
        _firstPendingLevel = maximumLevel + 1;
    }

    private void EnqueueIfChanged(ChunkColumnCoord column)
    {
        var currentLevel = GetTrackedLevel(column);
        var computedLevel = CalculateLevel(column);
        if (currentLevel == computedLevel)
        {
            RemovePendingUpdate(column);
            return;
        }

        var priorityLevel = Math.Min(currentLevel, computedLevel);
        if (_pendingUpdates.TryGetValue(column, out var existing))
        {
            if (existing.ComputedLevel == computedLevel &&
                existing.PriorityLevel == priorityLevel)
                return;

            _pendingByLevel[existing.PriorityLevel].Remove(column);
            _pendingUpdates.Remove(column);
        }

        _pendingUpdates.Add(
            column,
            new PendingDistanceUpdate(computedLevel, priorityLevel));
        _pendingByLevel[priorityLevel].Add(column);
        _firstPendingLevel = Math.Min(_firstPendingLevel, priorityLevel);
    }

    private void RemovePendingUpdate(ChunkColumnCoord column)
    {
        if (!_pendingUpdates.Remove(column, out var update))
            return;
        _pendingByLevel[update.PriorityLevel].Remove(column);
    }

    private ChunkDistanceUpdateBatch RunPendingUpdates(int maximumUpdates)
    {
        var previousLevels = new Dictionary<ChunkColumnCoord, int?>();
        var processedUpdates = 0;

        while (processedUpdates < maximumUpdates &&
               TryDequeuePendingUpdate(out var column))
        {
            processedUpdates++;
            var currentLevel = GetTrackedLevel(column);
            var computedLevel = CalculateLevel(column);
            if (currentLevel == computedLevel)
                continue;

            previousLevels.TryAdd(
                column,
                currentLevel <= MaximumLevel ? currentLevel : null);

            if (computedLevel > MaximumLevel)
                _levels.Remove(column);
            else
                _levels[column] = computedLevel;

            foreach (var neighbor in GetNeighbors(column))
                EnqueueIfChanged(neighbor);
        }

        return new ChunkDistanceUpdateBatch(
            processedUpdates,
            CreateChanges(previousLevels, _levels));
    }

    private bool TryDequeuePendingUpdate(out ChunkColumnCoord column)
    {
        while (_firstPendingLevel < _pendingByLevel.Length)
        {
            var level = _pendingByLevel[_firstPendingLevel];
            if (level.Count == 0)
            {
                _firstPendingLevel++;
                continue;
            }

            column = level.Min;
            level.Remove(column);
            if (!_pendingUpdates.Remove(column))
                throw new InvalidOperationException(
                    $"Pending distance update lost its queue entry: {column}.");
            return true;
        }

        column = default;
        return false;
    }

    private int GetTrackedLevel(ChunkColumnCoord column) =>
        _levels.TryGetValue(column, out var level)
            ? level
            : MaximumLevel + 1;

    private int CalculateLevel(ChunkColumnCoord column)
    {
        var best = _sources.TryGetValue(column, out var sourceLevel)
            ? sourceLevel
            : MaximumLevel + 1;

        foreach (var neighbor in GetNeighbors(column))
        {
            if (!_levels.TryGetValue(neighbor, out var neighborLevel) ||
                neighborLevel >= MaximumLevel)
                continue;

            best = Math.Min(best, neighborLevel + 1);
        }

        return best;
    }

    private static IEnumerable<ChunkColumnCoord> GetNeighbors(
        ChunkColumnCoord column)
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

            yield return ChunkColumnCoord.FromChunk(
                new ChunkCoord((int)x, 0, (int)z));
        }
    }

    private readonly record struct PendingDistanceUpdate(
        int ComputedLevel,
        int PriorityLevel);

    private static IReadOnlyList<ChunkDistanceLevelChange> CreateChanges(
        IReadOnlyDictionary<ChunkColumnCoord, int?> previous,
        IReadOnlyDictionary<ChunkColumnCoord, int> current)
    {
        var changes = new List<ChunkDistanceLevelChange>();
        foreach (var (column, previousLevel) in previous
                     .OrderBy(entry => entry.Key.X)
                     .ThenBy(entry => entry.Key.Z))
        {
            var hasCurrent = current.TryGetValue(column, out var currentLevel);
            if (previousLevel == (hasCurrent ? currentLevel : null))
                continue;

            changes.Add(new ChunkDistanceLevelChange(
                column,
                previousLevel,
                hasCurrent ? currentLevel : null));
        }

        return changes;
    }

    private static IReadOnlyList<ChunkDistanceLevelChange> CreateChanges(
        IReadOnlyDictionary<ChunkColumnCoord, int> previous,
        IReadOnlyDictionary<ChunkColumnCoord, int> current)
    {
        var changes = new List<ChunkDistanceLevelChange>();
        foreach (var column in previous.Keys.Concat(current.Keys)
                     .Distinct()
                     .OrderBy(column => column.X)
                     .ThenBy(column => column.Z))
        {
            var hadPrevious = previous.TryGetValue(column, out var previousLevel);
            var hasCurrent = current.TryGetValue(column, out var currentLevel);
            if (hadPrevious == hasCurrent &&
                (!hadPrevious || previousLevel == currentLevel))
                continue;

            changes.Add(new ChunkDistanceLevelChange(
                column,
                hadPrevious ? previousLevel : null,
                hasCurrent ? currentLevel : null));
        }

        return changes;
    }
}
