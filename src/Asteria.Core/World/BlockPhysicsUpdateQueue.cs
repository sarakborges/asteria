namespace Asteria.Core.World;

public sealed class BlockPhysicsUpdateQueue
{
    // Support checks can enqueue many voxels when chunks enter residency.
    // Keep the FIFO workload bounded on each tick; pending work remains queued.
    public const int MaximumBatchSize = 256;

    private static readonly (int X, int Y, int Z)[] VoxelEditWakeOffsets =
    [
        (0, 0, 0),
        (0, 1, 0),
        (0, -1, 0),
        (1, 0, 0),
        (-1, 0, 0),
        (0, 0, 1),
        (0, 0, -1),
    ];

    private readonly DeduplicatedQueue<WorldVoxelCoord>
        _queue = new();
    // Missing support chunks are dormant until their residency changes.
    private readonly Dictionary<ChunkCoord, HashSet<WorldVoxelCoord>>
        _waitingByChunk = [];
    private readonly Dictionary<WorldVoxelCoord, ChunkCoord>
        _waitingForChunk = [];

    public BlockPhysicsUpdateQueue(BlockPhysicsUpdateQueueSnapshot? restore = null)
    {
        if (restore is null) return;
        foreach (var position in restore.Positions)
            _queue.Enqueue(position);
    }

    // Pending and dormant work are both preserved across Sphere retirement.
    // The save format bounds snapshots, so capture a deterministic prefix if
    // streaming has produced work faster than physics can drain it. Any omitted
    // wakeups are derived from voxel state and are regenerated as chunks become
    // resident again.
    public BlockPhysicsUpdateQueueSnapshot CaptureState()
    {
        var maximumEntries = BlockPhysicsUpdateQueueSnapshot.MaximumEntries;
        var positions = new List<WorldVoxelCoord>(
            Math.Min(maximumEntries, _queue.Count + _waitingForChunk.Count));
        var captured = new HashSet<WorldVoxelCoord>();

        // Keep the authoritative FIFO order ahead of dormant dependencies.
        foreach (var position in _queue.ValuesInOrder())
        {
            if (position.Y < 0)
                throw new InvalidOperationException(
                    "Block-physics queue contains a position below the world.");

            if (!captured.Add(position))
                continue;

            positions.Add(position);
            if (positions.Count == maximumEntries)
                return new BlockPhysicsUpdateQueueSnapshot(positions);
        }

        // The two collections should be disjoint. Treat an overlap as one
        // wakeup, preserving the queued copy and its FIFO priority.
        foreach (var position in _waitingForChunk.Keys
            .OrderBy(position => position.Y)
            .ThenBy(position => position.Z)
            .ThenBy(position => position.X))
        {
            if (position.Y < 0)
                throw new InvalidOperationException(
                    "Block-physics wait list contains a position below the world.");

            if (!captured.Add(position))
                continue;

            positions.Add(position);
            if (positions.Count == maximumEntries)
                break;
        }

        return new BlockPhysicsUpdateQueueSnapshot(positions);
    }

    public int Count => _queue.Count;

    public int WaitingCount => _waitingForChunk.Count;

    public void WaitForChunk(WorldVoxelCoord position, ChunkCoord neededChunk)
    {
        // Invalid world-height dependencies must never enter persisted queue state.
        if (position.Y < 0 || neededChunk.Y < 0)
        {
            _queue.Remove(position);
            if (_waitingForChunk.TryGetValue(position, out var invalidPreviousChunk))
                RemoveWaiter(position, invalidPreviousChunk);
            return;
        }

        _queue.Remove(position);
        if (_waitingForChunk.TryGetValue(position, out var previousChunk))
        {
            if (previousChunk == neededChunk)
                return;
            RemoveWaiter(position, previousChunk);
        }

        if (!_waitingByChunk.TryGetValue(neededChunk, out var waiting))
        {
            waiting = [];
            _waitingByChunk.Add(neededChunk, waiting);
        }

        waiting.Add(position);
        _waitingForChunk.Add(position, neededChunk);
    }

    public void WakeResidentChunk(ChunkCoord chunk)
    {
        if (!_waitingByChunk.Remove(chunk, out var waiting))
            return;

        // Dependency activation must not depend on hash-set iteration.
        foreach (var position in waiting
            .OrderBy(position => position.Y)
            .ThenBy(position => position.Z)
            .ThenBy(position => position.X))
        {
            _waitingForChunk.Remove(position);
            _queue.Enqueue(position);
        }
    }

    private void RemoveWaiter(WorldVoxelCoord position, ChunkCoord chunk)
    {
        _waitingForChunk.Remove(position);
        var waiting = _waitingByChunk[chunk];
        waiting.Remove(position);
        if (waiting.Count == 0)
            _waitingByChunk.Remove(chunk);
    }

    public void EnqueueVoxelEdit(WorldVoxelCoord position)
    {
        // Authoritative edits must wake dependent blocks before the bulk scan
        // queued when a chunk entered residency. Promote already queued cells
        // too; ordinary Enqueue intentionally preserves FIFO order for chunk
        // seeding and capacity retries.
        // EnqueueFront reverses insertion order, so walk backwards to preserve
        // the deterministic changed-voxel/neighbor order exposed by DrainBatch.
        for (var index = VoxelEditWakeOffsets.Length - 1; index >= 0; index--)
            EnqueueFront(position + VoxelEditWakeOffsets[index]);
    }

    private void EnqueueFront(WorldVoxelCoord position)
    {
        if (position.Y < 0)
            return;

        if (_waitingForChunk.TryGetValue(position, out var neededChunk))
            RemoveWaiter(position, neededChunk);

        _queue.EnqueueFront(position);
    }

    public void Enqueue(WorldVoxelCoord position)
    {
        if (position.Y >= 0)
        {
            if (_waitingForChunk.TryGetValue(position, out var neededChunk))
                RemoveWaiter(position, neededChunk);
            _queue.Enqueue(position);
        }
    }

    public IReadOnlyList<WorldVoxelCoord> DrainBatch() =>
        _queue.Drain(MaximumBatchSize);
}
