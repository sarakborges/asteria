namespace Asteria.Core.World;

public sealed class BlockPhysicsUpdateQueue
{
    // Support checks can enqueue many voxels when chunks enter residency.
    // Keep the FIFO workload bounded on each tick; pending work remains queued.
    public const int MaximumBatchSize = 256;

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
    public BlockPhysicsUpdateQueueSnapshot CaptureState() =>
        new(_queue.ValuesInOrder()
            .Concat(_waitingForChunk.Keys
                .OrderBy(position => position.Y)
                .ThenBy(position => position.Z)
                .ThenBy(position => position.X))
            .ToArray());

    public int Count => _queue.Count;

    public int WaitingCount => _waitingForChunk.Count;

    public void WaitForChunk(WorldVoxelCoord position, ChunkCoord neededChunk)
    {
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
        WorldVoxelCoord[] affected =
        [
            position,
            position + (0, 1, 0),
            position + (0, -1, 0),
            // Any edited voxel may be the supporting side of wall flora.
            position + (1, 0, 0),
            position + (-1, 0, 0),
            position + (0, 0, 1),
            position + (0, 0, -1),
        ];

        // EnqueueFront reverses insertion order, so walk backwards to preserve
        // the deterministic changed-voxel/neighbor order exposed by DrainBatch.
        for (var index = affected.Length - 1; index >= 0; index--)
            EnqueueFront(affected[index]);
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
