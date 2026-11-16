namespace Asteria.Core.World;

public readonly record struct ChunkDistanceLevelChange(
    ChunkColumnCoord Column,
    int? PreviousLevel,
    int? Level);

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
        int maximumLevel) =>
        SynchronizeSourcesCore(tickets, graphFlag, maximumLevel, captureChanges: false);

    /// <summary>
    /// Synchronizes ticket sources and returns only nodes whose effective
    /// distance level changed. Holder maps can consume this delta without
    /// rescanning every node in the settled graph.
    /// </summary>
    public IReadOnlyList<ChunkDistanceLevelChange> SynchronizeSourcesWithChanges(
        ChunkTicketStorage tickets,
        ChunkTicketFlags graphFlag,
        int maximumLevel) =>
        SynchronizeSourcesCore(tickets, graphFlag, maximumLevel, captureChanges: true);

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
            return previousFullGraph is null
                ? Array.Empty<ChunkDistanceLevelChange>()
                : CreateChanges(previousFullGraph, _levels);

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

        var previousAffected = captureChanges
            ? new Dictionary<ChunkColumnCoord, int>()
            : null;
        foreach (var column in affected)
        {
            if (previousAffected is not null &&
                _levels.TryGetValue(column, out var previousLevel))
                previousAffected.Add(column, previousLevel);
            _levels.Remove(column);
        }

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

        if (previousFullGraph is not null)
            return CreateChanges(previousFullGraph, _levels);
        if (previousAffected is null)
            return Array.Empty<ChunkDistanceLevelChange>();

        var currentAffected = new Dictionary<ChunkColumnCoord, int>();
        foreach (var affectedColumn in affected)
            if (_levels.TryGetValue(affectedColumn, out var currentLevel))
                currentAffected.Add(affectedColumn, currentLevel);
        return CreateChanges(previousAffected, currentAffected);
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
