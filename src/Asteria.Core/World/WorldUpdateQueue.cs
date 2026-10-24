namespace Asteria.Core.World;

public sealed class WorldUpdateQueue
{
    private readonly DeduplicatedQueue<WorldVoxelCoord>
        _priorityLightingEdits = new();
    private readonly DeduplicatedQueue<WorldVoxelCoord>
        _lightingEdits = new();
    private readonly SortedDictionary<ChunkCoord, ChunkMeshletMask>
        _priorityMeshlets =
            new(ChunkCoordOrdering.YThenZThenX);
    private readonly SortedDictionary<ChunkCoord, ChunkMeshletMask>
        _backgroundMeshlets =
            new(ChunkCoordOrdering.YThenZThenX);

    public bool HasWork =>
        HasLightingWork ||
        HasMeshWork;

    public bool HasLightingWork => false;

    public bool HasMeshWork =>
        HasPriorityMeshWork ||
        HasBackgroundMeshWork;

    public bool HasPriorityMeshWork =>
        _priorityMeshlets.Count > 0;

    public bool HasBackgroundMeshWork =>
        _backgroundMeshlets.Count > 0;

    public int LightingEditCount => 0;

    public int DirtyChunkCount
    {
        get
        {
            var count =
                _priorityMeshlets.Count;

            foreach (var coord in
                     _backgroundMeshlets.Keys)
            {
                if (!_priorityMeshlets.ContainsKey(
                        coord))
                {
                    count++;
                }
            }

            return count;
        }
    }

    private readonly Dictionary<ChunkCoord, int> _priorityLightingSeedsByChunk = [];
    private readonly Dictionary<ChunkCoord, int> _lightingSeedsByChunk = [];

    public bool HasLightingWorkInChunks(Func<ChunkCoord, bool> includesChunk)
    {
        ArgumentNullException.ThrowIfNull(includesChunk);
        return false;
    }

    public bool HasMeshWorkInChunks(Func<ChunkCoord, bool> includesChunk)
    {
        ArgumentNullException.ThrowIfNull(includesChunk);
        return _priorityMeshlets.Keys.Any(includesChunk) ||
            _backgroundMeshlets.Keys.Any(includesChunk);
    }

    public void EnqueueVoxelEdit(
        VoxelWorld world,
        WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);

        EnqueueVoxelMeshlets(
            world,
            position,
            priority: true);
    }

    /// <summary>Visual-only surface edits do not alter voxel lighting, fluid topology or physics.</summary>
    public void EnqueueSurfaceEdit(VoxelWorld world, WorldVoxelCoord position) =>
        EnqueueVoxelMeshlets(world, position, priority: true);

    public void EnqueueLighting(WorldVoxelCoord position) { }

    public void EnqueueVoxelMeshlets(
        VoxelWorld world,
        WorldVoxelCoord position)
    {
        EnqueueVoxelMeshlets(
            world,
            position,
            priority: false);
    }

    public void EnqueueMeshlets(
        ChunkCoord coord,
        ChunkMeshletMask mask)
    {
        if (mask.IsEmpty)
        {
            return;
        }

        if (_priorityMeshlets.TryGetValue(
                coord,
                out var priority))
        {
            mask =
                mask.Except(
                    priority);

            if (mask.IsEmpty)
            {
                return;
            }
        }

        _backgroundMeshlets[coord] =
            _backgroundMeshlets.TryGetValue(
                coord,
                out var existing)
                ? existing.Union(mask)
                : mask;
    }

    /// <summary>
    /// Moves only existing background work into the priority lane. Becoming
    /// visible must not manufacture a second mesh build for work that is
    /// already in flight or has already been published.
    /// </summary>
    public ChunkMeshletMask PromoteBackgroundMeshlets(
        ChunkCoord coord,
        ChunkMeshletMask requested)
    {
        if (requested.IsEmpty ||
            !_backgroundMeshlets.TryGetValue(coord, out var background))
            return ChunkMeshletMask.None;

        var promoted = new ChunkMeshletMask(
            (byte)(background.Bits & requested.Bits));
        if (!promoted.IsEmpty)
            EnqueuePriorityMeshlets(coord, promoted);
        return promoted;
    }

    public void EnqueuePriorityMeshlets(
        ChunkCoord coord,
        ChunkMeshletMask mask)
    {
        if (mask.IsEmpty)
        {
            return;
        }

        if (_backgroundMeshlets.TryGetValue(
                coord,
                out var background))
        {
            var remaining =
                background.Except(mask);

            if (remaining.IsEmpty)
            {
                _backgroundMeshlets.Remove(
                    coord);
            }
            else
            {
                _backgroundMeshlets[coord] =
                    remaining;
            }
        }

        _priorityMeshlets[coord] =
            _priorityMeshlets.TryGetValue(
                coord,
                out var existing)
                ? existing.Union(mask)
                : mask;
    }

    public void RemoveMeshChunk(
        ChunkCoord coord)
    {
        _priorityMeshlets.Remove(coord);
        _backgroundMeshlets.Remove(coord);
    }

    // Retired chunks can leave substantial stale lighting work after fast
    // streaming movement. Discard those positions before snapshot capture,
    // but bound the inspection count to protect the main-thread budget.
    private const int MaximumLightingCandidatesPerDrain = 4096;

    public WorldLightingBatch DrainLighting(
        int maximumPositions = int.MaxValue,
        int maximumColumns = int.MaxValue,
        VoxelWorld? world = null,
        ChunkCoord? center = null,
        ChunkMovementDirection movement = default)
    {
        var fromPriorityLane = _priorityLightingEdits.Count > 0;
        var source = fromPriorityLane
            ? _priorityLightingEdits
            : _lightingEdits;
        var seedIndex = fromPriorityLane
            ? _priorityLightingSeedsByChunk
            : _lightingSeedsByChunk;
        var batch = DrainLightingFrom(
            source, maximumPositions, maximumColumns, world, center, movement,
            position => RemoveLightingSeed(seedIndex, position));
        return fromPriorityLane
            ? batch with { IsPriority = true }
            : batch;
    }

    private static WorldLightingBatch DrainLightingFrom(
        DeduplicatedQueue<WorldVoxelCoord> source,
        int maximumPositions,
        int maximumColumns,
        VoxelWorld? world,
        ChunkCoord? center,
        ChunkMovementDirection movement,
        Action<WorldVoxelCoord> onRemoved)
    {
        if (maximumPositions <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPositions));
        if (maximumColumns <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumColumns));

        if (world is null && maximumColumns == int.MaxValue)
        {
            var drained = source.Drain(maximumPositions);
            foreach (var position in drained)
                onRemoved(position);
            return new WorldLightingBatch(drained);
        }

        // Under streaming pressure, prefer columns near the current player
        // within the same bounded scan used for batching. This avoids
        // expensive lighting snapshots for distant work while keeping
        // unselected positions in stable FIFO order for fair fallback runs.
        HashSet<ChunkColumnCoord>? preferredColumns = null;
        if (center is { } focus)
        {
            var nearest = new List<(
                ChunkColumnCoord Column, ChunkLoadPriority Priority)>();
            source.VisitPrefix(MaximumLightingCandidatesPerDrain,
                position =>
                {
                    if (world is not null && !world.IsLoadedAt(position))
                        return;

                    var column = ChunkColumnCoord.FromChunk(
                        VoxelCoordinates.FromWorld(
                            position.X, position.Y, position.Z).Chunk);
                    if (nearest.Any(entry => entry.Column == column))
                        return;

                    // Lighting snapshots are grouped by horizontal column,
                    // so Y is intentionally fixed at the player's level.
                    var priority = ChunkStreamingState.Priority(
                        new ChunkCoord(column.X, focus.Y, column.Z),
                        focus, movement);
                    if (nearest.Count < maximumColumns)
                    {
                        nearest.Add((column, priority));
                        return;
                    }

                    var worst = 0;
                    for (var i = 1; i < nearest.Count; i++)
                    {
                        if (nearest[i].Priority.CompareTo(
                                nearest[worst].Priority) > 0)
                            worst = i;
                    }

                    if (priority.CompareTo(nearest[worst].Priority) < 0)
                        nearest[worst] = (column, priority);
                });
            preferredColumns = nearest.Select(entry => entry.Column).ToHashSet();
        }

        // Interleaved columns must not split a 512-seed worker into tiny
        // snapshots. Fill the chosen columns via bounded scan and remove
        // retired work without changing the order of deferred entries.
        var columns = new HashSet<ChunkColumnCoord>();
        return new WorldLightingBatch(source.DrainScanned(
            maximumPositions,
            MaximumLightingCandidatesPerDrain,
            position =>
            {
                if (world is not null && !world.IsLoadedAt(position))
                    return QueueScanDecision.Discard;

                var column = ChunkColumnCoord.FromChunk(
                    VoxelCoordinates.FromWorld(
                        position.X, position.Y, position.Z).Chunk);
                if (preferredColumns is not null)
                    return preferredColumns.Contains(column)
                        ? QueueScanDecision.Take
                        : QueueScanDecision.Keep;

                if (!columns.Contains(column) &&
                    columns.Count >= maximumColumns)
                    return QueueScanDecision.Keep;

                columns.Add(column);
                return QueueScanDecision.Take;
            },
            onRemoved));
    }

    public WorldMeshBatch DrainMeshlets(
        int maximumMeshlets = int.MaxValue,
        int maximumChunks = int.MaxValue)
    {
        ValidateMeshDrainLimits(maximumMeshlets, maximumChunks);

        var result = new Dictionary<ChunkCoord, ChunkMeshletMask>();
        var remaining = maximumMeshlets;
        MeshletMaskQueueDrain.Drain(
            _priorityMeshlets, result, ref remaining, maximumChunks);

        var remainingChunks = maximumChunks - result.Count;
        if (remaining > 0 && remainingChunks > 0)
            MeshletMaskQueueDrain.Drain(
                _backgroundMeshlets, result, ref remaining, remainingChunks);

        return new WorldMeshBatch(result);
    }

    public WorldMeshBatch DrainPriorityMeshlets(
        int maximumMeshlets = int.MaxValue,
        ChunkCoord? center = null,
        ChunkMovementDirection movement = default,
        int maximumChunks = int.MaxValue)
    {
        ValidateMeshDrainLimits(maximumMeshlets, maximumChunks);
        if (center is null)
            return DrainMeshletLane(
                _priorityMeshlets, maximumMeshlets, maximumChunks);

        var selected = new Dictionary<ChunkCoord, ChunkMeshletMask>();
        var remaining = maximumMeshlets;
        MeshletMaskQueueDrain.DrainNearest(
            _priorityMeshlets, selected, ref remaining, center.Value, movement,
            maximumChunks);
        return new WorldMeshBatch(selected);
    }

    public WorldMeshBatch DrainBackgroundMeshlets(
        int maximumMeshlets = int.MaxValue,
        int maximumChunks = int.MaxValue)
    {
        ValidateMeshDrainLimits(maximumMeshlets, maximumChunks);
        return DrainMeshletLane(
            _backgroundMeshlets, maximumMeshlets, maximumChunks);
    }

    public void RequeueLighting(
        WorldLightingBatch batch, VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(world);

        foreach (var position in batch.EditedPositions)
        {
            if (world.IsLoadedAt(position) &&
                _lightingEdits.Enqueue(position))
                AddLightingSeed(_lightingSeedsByChunk, position);
        }
    }

    public void RequeueMeshlets(
        WorldMeshBatch batch) =>
        RequeuePriorityMeshlets(batch);

    public void RequeuePriorityMeshlets(
        WorldMeshBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var (coord, mask) in
                 batch.DirtyMeshlets)
        {
            EnqueuePriorityMeshlets(
                coord,
                mask);
        }
    }

    public void RequeueBackgroundMeshlets(
        WorldMeshBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var (coord, mask) in
                 batch.DirtyMeshlets)
        {
            EnqueueMeshlets(
                coord,
                mask);
        }
    }

    private static WorldMeshBatch DrainMeshletLane(
        SortedDictionary<ChunkCoord, ChunkMeshletMask> source,
        int maximumMeshlets,
        int maximumChunks)
    {
        ValidateMeshDrainLimits(maximumMeshlets, maximumChunks);
        var result = new Dictionary<ChunkCoord, ChunkMeshletMask>();
        var remaining = maximumMeshlets;
        MeshletMaskQueueDrain.Drain(
            source, result, ref remaining, maximumChunks);
        return new WorldMeshBatch(result);
    }

    private static void ValidateMeshDrainLimits(
        int maximumMeshlets, int maximumChunks)
    {
        if (maximumMeshlets <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumMeshlets));
        if (maximumChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumChunks));
    }

    private static void AddLightingSeed(
        Dictionary<ChunkCoord, int> counts,
        WorldVoxelCoord position)
    {
        var coord = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z).Chunk;
        counts[coord] = counts.TryGetValue(coord, out var count)
            ? checked(count + 1)
            : 1;
    }

    private static void RemoveLightingSeed(
        Dictionary<ChunkCoord, int> counts,
        WorldVoxelCoord position)
    {
        var coord = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z).Chunk;
        if (!counts.TryGetValue(coord, out var count))
            throw new InvalidOperationException(
                $"Lighting seed index has no entry for {coord}.");

        if (count == 1)
            counts.Remove(coord);
        else
            counts[coord] = count - 1;
    }

    private void EnqueueVoxelMeshlets(
        VoxelWorld world,
        WorldVoxelCoord position,
        bool priority)
    {
        ArgumentNullException.ThrowIfNull(world);

        VoxelCoordinates
            .VisitChunkCoordsWhoseVoxelHaloContains(
                position,
                coord =>
                {
                    if (!world.ContainsChunk(coord))
                    {
                        return;
                    }

                    var mask =
                        ChunkMeshletMask.ForWorldPosition(
                            coord,
                            position);

                    if (priority)
                    {
                        EnqueuePriorityMeshlets(
                            coord,
                            mask);
                    }
                    else
                    {
                        EnqueueMeshlets(
                            coord,
                            mask);
                    }
                });
    }


}

public sealed record WorldLightingBatch(
    IReadOnlyList<WorldVoxelCoord> EditedPositions,
    bool IsPriority = false)
{
    public bool IsEmpty =>
        EditedPositions.Count == 0;
}

public sealed record WorldMeshBatch(
    IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask>
        DirtyMeshlets)
{
    public bool IsEmpty =>
        DirtyMeshlets.Count == 0;
}
