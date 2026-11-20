namespace Asteria.Core.World;

public readonly record struct BlockPhysicsWaitChunkCount(ChunkCoord Chunk, int Count);

public readonly record struct BlockPhysicsWaitDistribution(
    int SourceChunkCount,
    int TargetChunkCount,
    IReadOnlyList<BlockPhysicsWaitChunkCount> TopSourceChunks,
    IReadOnlyList<BlockPhysicsWaitChunkCount> TopTargetChunks);

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
    // Index every queued or dormant support check by its source chunk. When
    // that source chunk is archived, EnqueueResidentChunk will derive fresh
    // wakeups on restore; old waiters must not accumulate with player travel.
    private readonly Dictionary<ChunkCoord, HashSet<WorldVoxelCoord>>
        _positionsBySourceChunk = [];
    // Loaded support checks outside BLOCK_TICKING remain dormant until the
    // simulation graph raises their source holder back into range.
    private readonly Dictionary<ChunkCoord, HashSet<WorldVoxelCoord>>
        _waitingSimulationByChunk = [];
    private readonly Dictionary<WorldVoxelCoord, ChunkCoord>
        _waitingForSimulation = [];

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
            Math.Min(
                maximumEntries,
                _queue.Count + _waitingForChunk.Count + _waitingForSimulation.Count));
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

        foreach (var position in _waitingForSimulation.Keys
                     .OrderBy(position => position.Y)
                     .ThenBy(position => position.Z)
                     .ThenBy(position => position.X))
        {
            if (!captured.Add(position))
                continue;
            positions.Add(position);
            if (positions.Count == maximumEntries)
                break;
        }

        return new BlockPhysicsUpdateQueueSnapshot(positions);
    }

    public int Count => _queue.Count;

    public int WaitingCount =>
        _waitingForChunk.Count + _waitingForSimulation.Count;

    public IReadOnlyCollection<ChunkCoord> WaitingSimulationChunks =>
        _waitingSimulationByChunk.Keys.ToArray();

    public BlockPhysicsWaitDistribution CaptureWaitingDistribution(int maximumChunks = 3)
    {
        if (maximumChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumChunks));

        var sourceCounts = new Dictionary<ChunkCoord, int>();
        foreach (var position in _waitingForChunk.Keys)
        {
            var sourceChunk = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z).Chunk;
            sourceCounts[sourceChunk] = sourceCounts.GetValueOrDefault(sourceChunk) + 1;
        }

        var topSources = sourceCounts
            .Select(entry => new BlockPhysicsWaitChunkCount(entry.Key, entry.Value))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Chunk.Y)
            .ThenBy(entry => entry.Chunk.Z)
            .ThenBy(entry => entry.Chunk.X)
            .Take(maximumChunks)
            .ToArray();
        var topTargets = _waitingByChunk
            .Select(entry => new BlockPhysicsWaitChunkCount(entry.Key, entry.Value.Count))
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Chunk.Y)
            .ThenBy(entry => entry.Chunk.Z)
            .ThenBy(entry => entry.Chunk.X)
            .Take(maximumChunks)
            .ToArray();

        return new BlockPhysicsWaitDistribution(
            sourceCounts.Count, _waitingByChunk.Count, topSources, topTargets);
    }

    public void WaitForChunk(WorldVoxelCoord position, ChunkCoord neededChunk)
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
            existingChunk == neededChunk)
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
        TrackPosition(position);
    }

    /// <summary>
    /// Parks a loaded source chunk's support checks while its holder is outside
    /// BLOCK_TICKING. These positions are not part of the ready FIFO until the
    /// simulation distance graph promotes the source holder again.
    /// </summary>
    public void WaitForSimulationChunk(
        WorldVoxelCoord position,
        ChunkCoord sourceChunk)
    {
        if (position.Y < 0 || sourceChunk.Y < 0)
            return;
        if (_waitingForSimulation.TryGetValue(position, out var existing) &&
            existing == sourceChunk)
            return;

        _queue.Remove(position);
        UntrackPosition(position);
        if (_waitingForChunk.TryGetValue(position, out var previousChunk))
            RemoveWaiter(position, previousChunk);
        if (_waitingForSimulation.TryGetValue(position, out var previousSimulation))
            RemoveSimulationWaiter(position, previousSimulation);

        if (!_waitingSimulationByChunk.TryGetValue(sourceChunk, out var waiting))
        {
            waiting = [];
            _waitingSimulationByChunk.Add(sourceChunk, waiting);
        }

        waiting.Add(position);
        _waitingForSimulation[position] = sourceChunk;
        TrackPosition(position);
    }

    /// <summary>
    /// Reactivates simulation-dormant support work for every source holder that
    /// currently belongs to the BLOCK_TICKING graph.
    /// </summary>
    public int WakeSimulationChunks(
        Func<ChunkCoord, bool> isBlockTicking)
    {
        ArgumentNullException.ThrowIfNull(isBlockTicking);
        var reactivated = 0;
        foreach (var chunk in _waitingSimulationByChunk.Keys
                     .OrderBy(chunk => chunk.Y)
                     .ThenBy(chunk => chunk.Z)
                     .ThenBy(chunk => chunk.X)
                     .ToArray())
        {
            if (!isBlockTicking(chunk))
                continue;
            reactivated += WakeSimulationChunk(chunk);
        }

        return reactivated;
    }

    public int WakeSimulationChunk(ChunkCoord chunk)
    {
        if (!_waitingSimulationByChunk.Remove(chunk, out var waiting))
            return 0;

        var ordered = waiting
            .OrderBy(position => position.Y)
            .ThenBy(position => position.Z)
            .ThenBy(position => position.X)
            .ToArray();
        foreach (var position in ordered)
        {
            _waitingForSimulation.Remove(position);
            _queue.Enqueue(position);
        }

        return ordered.Length;
    }

    private void RemoveSimulationWaiter(
        WorldVoxelCoord position,
        ChunkCoord chunk)
    {
        _waitingForSimulation.Remove(position);
        if (!_waitingSimulationByChunk.TryGetValue(chunk, out var waiting))
            return;
        waiting.Remove(position);
        if (waiting.Count == 0)
            _waitingSimulationByChunk.Remove(chunk);
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
            if (_waitingForSimulation.TryGetValue(position, out var simulationChunk))
                RemoveSimulationWaiter(position, simulationChunk);
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
        if (_waitingForSimulation.TryGetValue(position, out var simulationChunk))
            RemoveSimulationWaiter(position, simulationChunk);

        _queue.EnqueueFront(position);
        TrackPosition(position);
    }

    public void Enqueue(WorldVoxelCoord position)
    {
        if (position.Y >= 0)
        {
            if (_waitingForChunk.TryGetValue(position, out var neededChunk))
                RemoveWaiter(position, neededChunk);
            if (_waitingForSimulation.TryGetValue(position, out var simulationChunk))
                RemoveSimulationWaiter(position, simulationChunk);
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
