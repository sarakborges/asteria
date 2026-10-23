namespace Asteria.Core.World;

/// <summary>
/// Change-driven natural leaf decay for one Sphere. Candidate and chunk-scan
/// queues contain only pending live-residency work: candidates are deduplicated
/// by voxel and removed when their chunk archives; chunk scans are removed on
/// archive too. There is no historical leaf registry and no per-frame world scan.
/// </summary>
public sealed class LeafDecayRuntime : IDisposable
{
    private enum SupportSearchResult : byte
    {
        Supported,
        Unsupported,
        Deferred,
    }

    private const int MaximumSupportDistance = 6;
    // A 13×13×13 support region gives the same radius in each voxel axis.
    private const int MaximumSupportSearchCells = 13 * 13 * 13;
    private const int MaximumCandidatesPerAdvance = 8;
    private const int MaximumChunkScansPerAdvance = 2;

    // Leaves may touch logs and one another along edges or corners. Use the
    // full 26-cell neighborhood both for support connectivity and mutation
    // invalidation; a face-only graph incorrectly decays diagonal foliage.
    private static readonly (int X, int Y, int Z)[] Neighbors =
    [
        (-1, -1, -1),
        (0, -1, -1),
        (1, -1, -1),
        (-1, -1, 0),
        (0, -1, 0),
        (1, -1, 0),
        (-1, -1, 1),
        (0, -1, 1),
        (1, -1, 1),
        (-1, 0, -1),
        (0, 0, -1),
        (1, 0, -1),
        (-1, 0, 0),
        (1, 0, 0),
        (-1, 0, 1),
        (0, 0, 1),
        (1, 0, 1),
        (-1, 1, -1),
        (0, 1, -1),
        (1, 1, -1),
        (-1, 1, 0),
        (0, 1, 0),
        (1, 1, 0),
        (-1, 1, 1),
        (0, 1, 1),
        (1, 1, 1),
    ];

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly VoxelMutationRuntime _mutations;
    private readonly BlockInteractionRuntime _interactions;
    private readonly HashSet<BlockRuntimeId> _leafSupportBlocks = [];
    private readonly DeduplicatedQueue<WorldVoxelCoord> _pendingLeaves = new();
    private readonly DeduplicatedQueue<ChunkCoord> _pendingChunkScans = new();
    private readonly Dictionary<ChunkCoord, HashSet<WorldVoxelCoord>> _pendingLeavesByChunk = [];
    private ulong _lastProcessedTick;
    private bool _disposed;

    public LeafDecayRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        VoxelMutationRuntime mutations,
        BlockInteractionRuntime interactions,
        ulong initialTick = 0)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _blocks = blocks ?? throw new ArgumentNullException(nameof(blocks));
        _mutations = mutations ?? throw new ArgumentNullException(nameof(mutations));
        _interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        _lastProcessedTick = initialTick;

        // A log's authored natural variant owns the support capability for all
        // variants in that same family (stripped and hollow included).
        var supportFamilies = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, definition) in _blocks.AuthoredDefinitions())
        {
            if (!definition.HasTag(TreeBlockTags.LeafSupport))
                continue;
            if (definition.Variant is { } variant)
                supportFamilies.Add(variant.Family);
        }
        foreach (var (runtimeId, definition) in _blocks.AuthoredDefinitions())
        {
            if (definition.HasTag(TreeBlockTags.LeafSupport) ||
                (definition.Variant is { } variant &&
                 supportFamilies.Contains(variant.Family)))
                _leafSupportBlocks.Add(runtimeId);
        }

        _world.ChunkBecameResident += OnChunkBecameResident;
        _world.ChunkBecameArchived += OnChunkBecameArchived;
        _mutations.BlockCellChanged += OnBlockCellChanged;

        foreach (var coord in _world.LoadedChunkCoords
                     .OrderBy(value => value.Y)
                     .ThenBy(value => value.Z)
                     .ThenBy(value => value.X))
            _pendingChunkScans.Enqueue(coord);
    }

    /// <summary>
    /// Advances bounded work only when the logical world tick changes and
    /// gameplay is active. Returns the number of leaves successfully removed.
    /// </summary>
    public int Advance(ulong worldTick, bool enabled = true)
    {
        if (_disposed || !enabled || worldTick <= _lastProcessedTick)
            return 0;

        _lastProcessedTick = worldTick;
        ScanPendingChunks(MaximumChunkScansPerAdvance);

        // Retry only after this batch, so a deferred candidate cannot consume
        // the whole per-tick budget when it is the only queued leaf.
        var budget = Math.Min(MaximumCandidatesPerAdvance, _pendingLeaves.Count);
        var retry = new List<WorldVoxelCoord>();
        var removed = 0;

        for (var index = 0; index < budget; index++)
        {
            if (!TryTakeCandidate(out var position))
                break;
            if (!_world.TryGetCell(position, out var cell) || cell.IsEmpty)
                continue;

            var definition = _blocks.GetDefinition(cell.Block);
            if (!definition.HasTag(TreeBlockTags.DecayableLeaf) ||
                cell.IsPlayerPlaced)
                continue;

            switch (FindSupport(position))
            {
                case SupportSearchResult.Supported:
                    continue;
                case SupportSearchResult.Deferred:
                    retry.Add(position);
                    continue;
                case SupportSearchResult.Unsupported:
                    var result = _interactions.Break(
                        new VoxelWorldHit(position, 0, 0, 0),
                        BlockBreakLootPolicy.DropSelf,
                        worldTick);
                    if (result.Accepted)
                        removed++;
                    else if (result.Rejection == BlockBreakRejection.MutationRejected)
                        retry.Add(position);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        foreach (var position in retry)
            EnqueueLeafCandidate(position);

        return removed;
    }

    private void ScanPendingChunks(int maximumScans)
    {
        for (var i = 0; i < maximumScans &&
                        _pendingChunkScans.TryDequeue(out var coord); i++)
        {
            if (!_world.TryGetChunk(coord, out var chunk))
                continue;

            var (originX, originY, originZ) = VoxelCoordinates.ChunkOrigin(coord);
            var candidates = new List<WorldVoxelCoord>();
            chunk.VisitBlockCells((x, y, z, cell) =>
            {
                if (cell.IsEmpty || cell.IsPlayerPlaced ||
                    !_blocks.GetDefinition(cell.Block).HasTag(TreeBlockTags.DecayableLeaf))
                    return;
                candidates.Add(new WorldVoxelCoord(
                    originX + x, originY + y, originZ + z));
            });
            candidates.Sort(ComparePosition);
            foreach (var candidate in candidates)
                EnqueueLeafCandidate(candidate);
        }
    }

    private void OnChunkBecameResident(ChunkCoord coord)
    {
        if (!_disposed)
            _pendingChunkScans.Enqueue(coord);
    }

    private void OnChunkBecameArchived(ChunkCoord coord)
    {
        if (_disposed)
            return;

        _pendingChunkScans.Remove(coord);
        if (!_pendingLeavesByChunk.Remove(coord, out var candidates))
            return;

        foreach (var candidate in candidates.OrderBy(value => value.Y)
                     .ThenBy(value => value.Z).ThenBy(value => value.X))
            _pendingLeaves.Remove(candidate);
    }

    private void OnBlockCellChanged(VoxelWorldEdit edit)
    {
        if (_disposed)
            return;

        var supportRemoved =
            IsLeafSupport(edit.Previous) && !IsLeafSupport(edit.Current);
        var previous = _blocks.GetDefinition(edit.Previous.Block);
        var current = _blocks.GetDefinition(edit.Current.Block);
        var leafRemoved =
            previous.HasTag(TreeBlockTags.DecayableLeaf) &&
            !current.HasTag(TreeBlockTags.DecayableLeaf);
        if (!supportRemoved && !leafRemoved)
            return;

        foreach (var (x, y, z) in Neighbors)
        {
            if (!TryOffset(edit.Position, x, y, z, out var neighbor))
                continue;
            EnqueueLeafCandidate(neighbor);
        }
    }

    private bool IsLeafSupport(VoxelCell cell) =>
        !cell.IsEmpty && _leafSupportBlocks.Contains(cell.Block);

    private void EnqueueLeafCandidate(WorldVoxelCoord position)
    {
        if (_disposed || position.Y < 0 ||
            !_world.TryGetCell(position, out var cell) || cell.IsEmpty ||
            cell.IsPlayerPlaced ||
            !_blocks.GetDefinition(cell.Block).HasTag(TreeBlockTags.DecayableLeaf) ||
            !_pendingLeaves.Enqueue(position))
            return;

        var chunk = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z).Chunk;
        if (!_pendingLeavesByChunk.TryGetValue(chunk, out var candidates))
            _pendingLeavesByChunk.Add(chunk, candidates = []);
        candidates.Add(position);
    }

    private bool TryTakeCandidate(out WorldVoxelCoord position)
    {
        if (!_pendingLeaves.TryDequeue(out position))
            return false;

        var chunk = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z).Chunk;
        if (_pendingLeavesByChunk.TryGetValue(chunk, out var candidates))
        {
            candidates.Remove(position);
            if (candidates.Count == 0)
                _pendingLeavesByChunk.Remove(chunk);
        }
        return true;
    }

    private SupportSearchResult FindSupport(WorldVoxelCoord origin)
    {
        var pending = new Queue<(WorldVoxelCoord Position, int Distance)>();
        var visited = new HashSet<WorldVoxelCoord>();
        pending.Enqueue((origin, 0));
        visited.Add(origin);
        var encounteredUnloadedSpace = false;

        while (pending.Count > 0 && visited.Count <= MaximumSupportSearchCells)
        {
            var (position, distance) = pending.Dequeue();
            if (!_world.TryGetCell(position, out var cell))
            {
                encounteredUnloadedSpace = true;
                continue;
            }
            if (cell.IsEmpty)
                continue;

            var definition = _blocks.GetDefinition(cell.Block);
            if (distance > 0 && IsLeafSupport(cell) &&
                !definition.HasTag(TreeBlockTags.DecayableLeaf))
                return SupportSearchResult.Supported;

            if (!definition.HasTag(TreeBlockTags.DecayableLeaf) ||
                distance >= MaximumSupportDistance)
                continue;

            foreach (var (x, y, z) in Neighbors)
            {
                if (!TryOffset(position, x, y, z, out var neighbor) ||
                    !visited.Add(neighbor))
                    continue;
                pending.Enqueue((neighbor, distance + 1));
            }
        }

        return encounteredUnloadedSpace
            ? SupportSearchResult.Deferred
            : SupportSearchResult.Unsupported;
    }

    private static bool TryOffset(
        WorldVoxelCoord position, int x, int y, int z,
        out WorldVoxelCoord result)
    {
        var nextX = (long)position.X + x;
        var nextY = (long)position.Y + y;
        var nextZ = (long)position.Z + z;
        if (nextX < int.MinValue || nextX > int.MaxValue ||
            nextY < 0 || nextY > int.MaxValue ||
            nextZ < int.MinValue || nextZ > int.MaxValue)
        {
            result = default;
            return false;
        }

        result = new WorldVoxelCoord((int)nextX, (int)nextY, (int)nextZ);
        return true;
    }

    private static int ComparePosition(WorldVoxelCoord left, WorldVoxelCoord right)
    {
        var y = left.Y.CompareTo(right.Y);
        if (y != 0) return y;
        var z = left.Z.CompareTo(right.Z);
        return z != 0 ? z : left.X.CompareTo(right.X);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _world.ChunkBecameResident -= OnChunkBecameResident;
        _world.ChunkBecameArchived -= OnChunkBecameArchived;
        _mutations.BlockCellChanged -= OnBlockCellChanged;
        _pendingLeaves.Clear();
        _pendingChunkScans.Clear();
        _pendingLeavesByChunk.Clear();
    }
}
