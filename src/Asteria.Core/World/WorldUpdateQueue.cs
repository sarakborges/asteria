namespace Asteria.Core.World;

public sealed class WorldUpdateQueue
{
    private readonly HashSet<WorldVoxelCoord> _lightingEdits = [];
    private readonly Dictionary<ChunkCoord, ChunkMeshletMask> _dirtyMeshlets = [];

    public bool HasWork =>
        _lightingEdits.Count > 0 || _dirtyMeshlets.Count > 0;

    public int LightingEditCount => _lightingEdits.Count;

    public int DirtyChunkCount => _dirtyMeshlets.Count;

    public void EnqueueVoxelEdit(
        VoxelWorld world,
        WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);

        _lightingEdits.Add(position);

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

        _dirtyMeshlets[coord] =
            _dirtyMeshlets.TryGetValue(coord, out var existing)
                ? existing.Union(mask)
                : mask;
    }

    public WorldUpdateBatch Drain()
    {
        var lighting = _lightingEdits.ToArray();
        var meshlets = new Dictionary<ChunkCoord, ChunkMeshletMask>(
            _dirtyMeshlets);

        _lightingEdits.Clear();
        _dirtyMeshlets.Clear();

        return new WorldUpdateBatch(lighting, meshlets);
    }

    public void Requeue(WorldUpdateBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var position in batch.LightingEdits)
        {
            _lightingEdits.Add(position);
        }

        foreach (var (coord, mask) in batch.DirtyMeshlets)
        {
            EnqueueMeshlets(coord, mask);
        }
    }
}

public sealed record WorldUpdateBatch(
    IReadOnlyList<WorldVoxelCoord> LightingEdits,
    IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> DirtyMeshlets)
{
    public bool IsEmpty =>
        LightingEdits.Count == 0 && DirtyMeshlets.Count == 0;
}
