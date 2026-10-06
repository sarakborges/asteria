namespace Asteria.Core.World;

public readonly record struct FluidTickKey(
    FluidRuntimeId Fluid,
    WorldVoxelCoord Position);

public sealed record FluidWorkBatch(
    IReadOnlyList<WorldVoxelCoord> TopologyPositions,
    IReadOnlyList<FluidTickKey> DueTicks)
{
    public bool IsEmpty =>
        TopologyPositions.Count == 0 &&
        DueTicks.Count == 0;

    public IEnumerable<WorldVoxelCoord> Positions =>
        TopologyPositions.Concat(
            DueTicks.Select(tick => tick.Position));
}

public sealed class FluidUpdateQueue
{
    private static readonly (int X, int Y, int Z)[] Neighborhood =
    [
        (0, 0, 0),
        (0, 1, 0),
        (0, -1, 0),
        (1, 0, 0),
        (-1, 0, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];

    private readonly HashSet<WorldVoxelCoord> _topology = [];
    private readonly SortedDictionary<ulong, ScheduledBucket> _scheduled = [];
    private readonly Dictionary<FluidTickKey, ulong> _scheduledDue = [];
    private readonly Dictionary<ChunkCoord, HashSet<FluidTickKey>> _dormant = [];

    public int Count =>
        _topology.Count +
        _scheduledDue.Count;

    public int TopologyCount => _topology.Count;

    public int ScheduledCount => _scheduledDue.Count;

    public int DormantChunkCount => _dormant.Count;

    public ulong? NextDueTick =>
        _scheduled.Count == 0
            ? null
            : _scheduled.First().Key;

    public bool HasReadyWork(ulong currentTick) =>
        _topology.Count > 0 ||
        (NextDueTick is { } due &&
         due <= currentTick);

    public void EnqueueTopology(
        WorldVoxelCoord position)
    {
        if (position.Y >= 0)
        {
            _topology.Add(position);
        }
    }

    public void EnqueueTopologyNeighborhood(
        WorldVoxelCoord position)
    {
        foreach (var offset in Neighborhood)
        {
            EnqueueTopology(position + offset);
        }
    }

    public void ScheduleAt(
        FluidTickKey key,
        ulong dueTick)
    {
        if (key.Fluid.IsNone ||
            key.Position.Y < 0)
        {
            return;
        }

        if (_scheduledDue.TryGetValue(
                key,
                out var existing) &&
            existing <= dueTick)
        {
            return;
        }

        _scheduledDue[key] = dueTick;

        if (!_scheduled.TryGetValue(
                dueTick,
                out var bucket))
        {
            bucket = new ScheduledBucket();
            _scheduled.Add(dueTick, bucket);
        }

        bucket.Push(key);
    }

    public void ScheduleNeighborhood(
        FluidRuntimeId fluid,
        WorldVoxelCoord position,
        ulong dueTick)
    {
        ScheduleAt(
            new FluidTickKey(fluid, position),
            dueTick);
        ScheduleAt(
            new FluidTickKey(
                fluid,
                position + (0, -1, 0)),
            dueTick);

        foreach (var offset in Neighborhood.AsSpan(3))
        {
            ScheduleAt(
                new FluidTickKey(
                    fluid,
                    position + offset),
                dueTick);
        }
    }

    public FluidWorkBatch DrainReady(
        ulong currentTick,
        int maximumItems)
    {
        if (maximumItems <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumItems));
        }

        var dueReady =
            NextDueTick is { } next &&
            next <= currentTick;

        var topologyBudget =
            dueReady
                ? Math.Min(
                    _topology.Count,
                    Math.Max(1, maximumItems / 2))
                : Math.Min(
                    _topology.Count,
                    maximumItems);

        var topology = _topology
            .OrderBy(position =>
                VoxelCoordinates.FromWorld(
                    position.X,
                    position.Y,
                    position.Z).Chunk.Y)
            .ThenBy(position =>
                VoxelCoordinates.FromWorld(
                    position.X,
                    position.Y,
                    position.Z).Chunk.Z)
            .ThenBy(position =>
                VoxelCoordinates.FromWorld(
                    position.X,
                    position.Y,
                    position.Z).Chunk.X)
            .ThenBy(position => position.Y)
            .ThenBy(position => position.Z)
            .ThenBy(position => position.X)
            .Take(topologyBudget)
            .ToArray();

        foreach (var position in topology)
        {
            _topology.Remove(position);
        }

        var remaining =
            maximumItems - topology.Length;
        var due = new List<FluidTickKey>(
            remaining);

        while (remaining > 0 &&
               _scheduled.Count > 0)
        {
            var first =
                _scheduled.First();

            if (first.Key > currentTick)
            {
                break;
            }

            var key =
                first.Value.Pop();

            if (first.Value.IsEmpty)
            {
                _scheduled.Remove(first.Key);
            }

            if (key is null)
            {
                continue;
            }

            if (!_scheduledDue.TryGetValue(
                    key.Value,
                    out var authoritativeDue) ||
                authoritativeDue != first.Key)
            {
                continue;
            }

            _scheduledDue.Remove(key.Value);
            due.Add(key.Value);
            remaining--;
        }

        return new FluidWorkBatch(
            topology,
            due);
    }

    public void RequeueTopology(
        IEnumerable<WorldVoxelCoord> positions)
    {
        ArgumentNullException.ThrowIfNull(positions);

        foreach (var position in positions)
        {
            EnqueueTopology(position);
        }
    }

    public void RequeueDue(
        IEnumerable<FluidTickKey> ticks,
        ulong dueTick)
    {
        ArgumentNullException.ThrowIfNull(ticks);

        foreach (var tick in ticks)
        {
            ScheduleAt(tick, dueTick);
        }
    }

    public void DeferUnloaded(
        FluidTickKey tick)
    {
        var coord =
            VoxelCoordinates.FromWorld(
                tick.Position.X,
                tick.Position.Y,
                tick.Position.Z).Chunk;

        if (!_dormant.TryGetValue(
                coord,
                out var ticks))
        {
            ticks = [];
            _dormant.Add(coord, ticks);
        }

        ticks.Add(tick);
    }

    public void ReactivateLoadedChunk(
        ChunkCoord coord,
        ulong currentTick)
    {
        if (!_dormant.Remove(
                coord,
                out var ticks))
        {
            return;
        }

        foreach (var tick in ticks)
        {
            ScheduleAt(
                tick,
                currentTick);
        }
    }

    private sealed class ScheduledBucket
    {
        private readonly Dictionary<ChunkCoord, Queue<FluidTickKey>>
            _byChunk = [];
        private readonly Queue<ChunkCoord> _activeChunks = [];
        private readonly HashSet<ChunkCoord> _activeSet = [];

        public bool IsEmpty =>
            _activeChunks.Count == 0;

        public void Push(FluidTickKey key)
        {
            var coord =
                VoxelCoordinates.FromWorld(
                    key.Position.X,
                    key.Position.Y,
                    key.Position.Z).Chunk;

            if (!_byChunk.TryGetValue(
                    coord,
                    out var queue))
            {
                queue = new Queue<FluidTickKey>();
                _byChunk.Add(coord, queue);
            }

            queue.Enqueue(key);

            if (_activeSet.Add(coord))
            {
                _activeChunks.Enqueue(coord);
            }
        }

        public FluidTickKey? Pop()
        {
            while (_activeChunks.Count > 0)
            {
                var coord =
                    _activeChunks.Dequeue();
                _activeSet.Remove(coord);

                if (!_byChunk.TryGetValue(
                        coord,
                        out var queue) ||
                    queue.Count == 0)
                {
                    _byChunk.Remove(coord);
                    continue;
                }

                var key = queue.Dequeue();

                if (queue.Count > 0)
                {
                    _activeChunks.Enqueue(coord);
                    _activeSet.Add(coord);
                }
                else
                {
                    _byChunk.Remove(coord);
                }

                return key;
            }

            return null;
        }
    }
}
