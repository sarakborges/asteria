namespace Asteria.Core.World;

public enum BlockPhysicsWaitReason : byte
{
    SupportDependency = 0,
    GravityBelowChunk = 1,
}

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
    private readonly Dictionary<WorldVoxelCoord, BlockPhysicsWaitReason>
        _waitingReasonByPosition = [];
    private int _waitingSupportCount;
    private int _waitingGravityCount;
    // Index every queued or dormant support check by its source chunk. When
    // that source chunk is archived, EnqueueResidentChunk will derive fresh
    // wakeups on restore; old waiters must not accumulate with player travel.
    private readonly Dictionary<ChunkCoord, HashSet<WorldVoxelCoord>>
        _positionsBySourceChunk = [];

    public BlockPhysicsUpdateQueue(BlockPhysicsUpdateQueueSnapshot? restore = null)
    {
        if (restore is null) return;
        foreach (var position in restore.Positions)
        {
            _queue.Enqueue(position);
            TrackPosition(position);
        }
    }

    // Queue snapshot support remains for legacy-format validation. The live
    // session save path deliberately does not persist this derived work: chunk
    // activation recomputes gravity/support wakeups from authoritative voxels.
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

    public int WaitingSupportCount => _waitingSupportCount;

    public int WaitingGravityCount => _waitingGravityCount;

    public void WaitForChunk(
        WorldVoxelCoord position,
        ChunkCoord neededChunk,
        BlockPhysicsWaitReason reason = BlockPhysicsWaitReason.SupportDependency)
    {
        // Invalid world-height dependencies must never enter persisted queue state.
        if (position.Y < 0 || neededChunk.Y < 0)
        {
            _queue.Remove(position);
            if (_waitingForChunk.TryGetValue(position, out var invalidPreviousChunk))
                RemoveWaiter(position, invalidPreviousChunk);
            UntrackPosition(position);
            return;
        }

        if (_waitingForChunk.TryGetValue(position, out var existingChunk) &&
            existingChunk == neededChunk &&
            _waitingReasonByPosition.TryGetValue(position, out var existingReason) &&
            existingReason == reason)
            return;

        _queue.Remove(position);
        UntrackPosition(position);
        if (_waitingForChunk.TryGetValue(position, out var previousChunk))
            RemoveWaiter(position, previousChunk);

        if (!_waitingByChunk.TryGetValue(neededChunk, out var waiting))
        {
            waiting = [];
            _waitingByChunk.Add(neededChunk, waiting);
        }

        waiting.Add(position);
        _waitingForChunk.Add(position, neededChunk);
        TrackWaitReason(position, reason);
        TrackPosition(position);
    }

    /// <summary>
    /// Removes stale wakeups owned by a source chunk that has left residency.
    /// The source's block state is archived and will be rescanned on restore.
    /// </summary>
    public int ForgetSourceChunk(ChunkCoord sourceChunk)
    {
        if (!_positionsBySourceChunk.Remove(sourceChunk, out var positions))
            return 0;

        foreach (var position in positions)
        {
            _queue.Remove(position);
            if (_waitingForChunk.TryGetValue(position, out var neededChunk))
                RemoveWaiter(position, neededChunk);
        }

        return positions.Count;
    }

    private void TrackPosition(WorldVoxelCoord position)
    {
        if (position.Y < 0)
            return;

        var sourceChunk = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z).Chunk;
        if (!_positionsBySourceChunk.TryGetValue(sourceChunk, out var positions))
        {
            positions = [];
            _positionsBySourceChunk.Add(sourceChunk, positions);
        }
        positions.Add(position);
    }

    private void TrackWaitReason(
        WorldVoxelCoord position,
        BlockPhysicsWaitReason reason)
    {
        _waitingReasonByPosition[position] = reason;
        switch (reason)
        {
            case BlockPhysicsWaitReason.SupportDependency:
                _waitingSupportCount++;
                break;
            case BlockPhysicsWaitReason.GravityBelowChunk:
                _waitingGravityCount++;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
        }
    }

    private void UntrackWaitReason(WorldVoxelCoord position)
    {
        if (!_waitingReasonByPosition.Remove(position, out var reason))
            return;

        switch (reason)
        {
            case BlockPhysicsWaitReason.SupportDependency:
                _waitingSupportCount--;
                break;
            case BlockPhysicsWaitReason.GravityBelowChunk:
                _waitingGravityCount--;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(reason), reason, null);
        }
    }

    private void UntrackPosition(WorldVoxelCoord position)
    {
        if (position.Y < 0)
            return;

        var sourceChunk = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z).Chunk;
        if (!_positionsBySourceChunk.TryGetValue(sourceChunk, out var positions))
            return;

        positions.Remove(position);
        if (positions.Count == 0)
            _positionsBySourceChunk.Remove(sourceChunk);
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
            UntrackWaitReason(position);
            _queue.Enqueue(position);
        }
    }

    private void RemoveWaiter(WorldVoxelCoord position, ChunkCoord chunk)
    {
        _waitingForChunk.Remove(position);
        UntrackWaitReason(position);
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
        TrackPosition(position);
    }

    public void Enqueue(WorldVoxelCoord position)
    {
        if (position.Y >= 0)
        {
            if (_waitingForChunk.TryGetValue(position, out var neededChunk))
                RemoveWaiter(position, neededChunk);
            _queue.Enqueue(position);
            TrackPosition(position);
        }
    }

    public IReadOnlyList<WorldVoxelCoord> DrainBatch()
    {
        var drained = _queue.Drain(MaximumBatchSize);
        foreach (var position in drained)
            UntrackPosition(position);
        return drained;
    }
}
