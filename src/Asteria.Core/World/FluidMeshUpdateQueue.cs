namespace Asteria.Core.World;

public sealed class FluidMeshUpdateQueue
{
    private readonly Dictionary<ChunkCoord, ChunkMeshletMask>
        _dirty = [];

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

    public WorldMeshBatch Drain()
    {
        var batch =
            new Dictionary<ChunkCoord, ChunkMeshletMask>(
                _dirty);
        _dirty.Clear();
        return new WorldMeshBatch(batch);
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
