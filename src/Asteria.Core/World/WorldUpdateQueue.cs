namespace Asteria.Core.World;

public sealed class WorldUpdateQueue
{
    private readonly SortedDictionary<ChunkCoord, ChunkMeshletMask>
        _priorityMeshlets =
            new(ChunkCoordOrdering.YThenZThenX);
    private readonly SortedDictionary<ChunkCoord, ChunkMeshletMask>
        _backgroundMeshlets =
            new(ChunkCoordOrdering.YThenZThenX);

    public bool HasWork => HasMeshWork;

    public bool HasMeshWork =>
        HasPriorityMeshWork ||
        HasBackgroundMeshWork;

    public bool HasPriorityMeshWork =>
        _priorityMeshlets.Count > 0;

    public bool HasBackgroundMeshWork =>
        _backgroundMeshlets.Count > 0;


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

    /// <summary>Visual-only surface edits enqueue mesh work without changing voxel state.</summary>
    public void EnqueueSurfaceEdit(VoxelWorld world, WorldVoxelCoord position) =>
        EnqueueVoxelMeshlets(world, position, priority: true);

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

public sealed record WorldMeshBatch(
    IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask>
        DirtyMeshlets)
{
    public bool IsEmpty =>
        DirtyMeshlets.Count == 0;
}

