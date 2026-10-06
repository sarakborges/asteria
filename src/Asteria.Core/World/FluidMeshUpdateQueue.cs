namespace Asteria.Core.World;

public sealed class FluidMeshUpdateQueue
{
    private readonly SortedDictionary<ChunkCoord, ChunkMeshletMask>
        _dirty =
            new(ChunkCoordOrdering.YThenZThenX);

    public bool HasWork => _dirty.Count > 0;

    public int DirtyChunkCount => _dirty.Count;

    public void EnqueueVoxelEdit(
        VoxelWorld world,
        WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);

        VoxelCoordinates.VisitChunkCoordsWhoseVoxelHaloContains(
            position,
            coord =>
            {
                if (!world.ContainsChunk(coord))
                {
                    return;
                }

                EnqueueMeshlets(
                    coord,
                    ChunkMeshletMask.ForWorldPosition(
                        coord,
                        position));
            });
    }

    public void EnqueueMeshlets(
        ChunkCoord coord,
        ChunkMeshletMask mask)
    {
        if (mask.IsEmpty)
        {
            return;
        }

        _dirty[coord] =
            _dirty.TryGetValue(
                coord,
                out var existing)
                ? existing.Union(mask)
                : mask;
    }

    public void RemoveChunk(ChunkCoord coord)
    {
        _dirty.Remove(coord);
    }

    public WorldMeshBatch Drain(
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
            _dirty,
            result,
            ref remaining);

        return new WorldMeshBatch(result);
    }

    public void Requeue(WorldMeshBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var (coord, mask) in batch.DirtyMeshlets)
        {
            EnqueueMeshlets(coord, mask);
        }
    }
}
