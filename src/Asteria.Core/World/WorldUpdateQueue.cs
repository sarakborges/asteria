namespace Asteria.Core.World;

public sealed class WorldUpdateQueue
{
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

    public bool HasLightingWork =>
        _lightingEdits.Count > 0;

    public bool HasMeshWork =>
        HasPriorityMeshWork ||
        HasBackgroundMeshWork;

    public bool HasPriorityMeshWork =>
        _priorityMeshlets.Count > 0;

    public bool HasBackgroundMeshWork =>
        _backgroundMeshlets.Count > 0;

    public int LightingEditCount =>
        _lightingEdits.Count;

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

    public void EnqueueVoxelEdit(
        VoxelWorld world,
        WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);

        _lightingEdits.Enqueue(position);
        EnqueueVoxelMeshlets(
            world,
            position,
            priority: true);
    }

    /// <summary>Visual-only surface edits do not alter voxel lighting, fluid topology or physics.</summary>
    public void EnqueueSurfaceEdit(VoxelWorld world, WorldVoxelCoord position) =>
        EnqueueVoxelMeshlets(world, position, priority: true);

    public void EnqueueLighting(
        WorldVoxelCoord position)
    {
        _lightingEdits.Enqueue(position);
    }

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
        VoxelWorld? world = null)
    {
        if (maximumPositions <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumPositions));
        if (maximumColumns <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumColumns));

        if (world is null)
        {
            if (maximumColumns == int.MaxValue)
                return new WorldLightingBatch(
                    _lightingEdits.Drain(maximumPositions));

            var selectedColumns = new HashSet<ChunkColumnCoord>();
            return new WorldLightingBatch(
                _lightingEdits.DrainPrefix(
                    maximumPositions,
                    position =>
                    {
                        var column = ChunkColumnCoord.FromChunk(
                            VoxelCoordinates.FromWorld(
                                position.X, position.Y, position.Z).Chunk);
                        return selectedColumns.Contains(column) ||
                            selectedColumns.Count < maximumColumns &&
                            selectedColumns.Add(column);
                    }));
        }

        var selected = new List<WorldVoxelCoord>(
            Math.Min(maximumPositions, MaximumLightingCandidatesPerDrain));
        var columns = new HashSet<ChunkColumnCoord>();
        var examined = 0;

        while (selected.Count < maximumPositions &&
               examined < MaximumLightingCandidatesPerDrain &&
               _lightingEdits.TryPeek(out var position))
        {
            examined++;
            if (!world.IsLoadedAt(position))
            {
                _lightingEdits.TryDequeue(out _);
                continue;
            }

            var column = ChunkColumnCoord.FromChunk(
                VoxelCoordinates.FromWorld(
                    position.X, position.Y, position.Z).Chunk);
            if (!columns.Contains(column) &&
                columns.Count >= maximumColumns)
                break;

            columns.Add(column);
            _lightingEdits.TryDequeue(out _);
            selected.Add(position);
        }

        return new WorldLightingBatch(selected);
    }

    public WorldMeshBatch DrainMeshlets(
        int maximumMeshlets = int.MaxValue)
    {
        if (maximumMeshlets <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumMeshlets));
        }

        var result =
            new Dictionary<ChunkCoord, ChunkMeshletMask>();
        var remaining =
            maximumMeshlets;

        MeshletMaskQueueDrain.Drain(
            _priorityMeshlets,
            result,
            ref remaining);

        if (remaining > 0)
        {
            MeshletMaskQueueDrain.Drain(
                _backgroundMeshlets,
                result,
                ref remaining);
        }

        return new WorldMeshBatch(result);
    }

    public WorldMeshBatch DrainPriorityMeshlets(
        int maximumMeshlets = int.MaxValue,
        ChunkCoord? center = null,
        ChunkMovementDirection movement = default)
    {
        if (center is null)
            return DrainMeshletLane(_priorityMeshlets, maximumMeshlets);
        if (maximumMeshlets <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumMeshlets));

        var selected = new Dictionary<ChunkCoord, ChunkMeshletMask>();
        var remaining = maximumMeshlets;
        MeshletMaskQueueDrain.DrainNearest(
            _priorityMeshlets, selected, ref remaining, center.Value, movement);
        return new WorldMeshBatch(selected);
    }

    public WorldMeshBatch DrainBackgroundMeshlets(
        int maximumMeshlets = int.MaxValue) =>
        DrainMeshletLane(
            _backgroundMeshlets,
            maximumMeshlets);

    public void RequeueLighting(
        WorldLightingBatch batch, VoxelWorld world)
    {
        ArgumentNullException.ThrowIfNull(batch);
        ArgumentNullException.ThrowIfNull(world);

        foreach (var position in batch.EditedPositions)
        {
            if (world.IsLoadedAt(position))
                _lightingEdits.Enqueue(position);
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
        int maximumMeshlets)
    {
        if (maximumMeshlets <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumMeshlets));
        }

        var result =
            new Dictionary<ChunkCoord, ChunkMeshletMask>();
        var remaining =
            maximumMeshlets;

        MeshletMaskQueueDrain.Drain(
            source,
            result,
            ref remaining);

        return new WorldMeshBatch(result);
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
    IReadOnlyList<WorldVoxelCoord> EditedPositions)
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
