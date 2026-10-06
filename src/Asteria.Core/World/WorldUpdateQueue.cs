namespace Asteria.Core.World;

public sealed class WorldUpdateQueue
{
    private readonly HashSet<WorldVoxelCoord> _lightingEdits = [];
    private readonly Dictionary<ChunkCoord, ChunkMeshletMask> _dirtyMeshlets = [];

    public bool HasWork => HasLightingWork || HasMeshWork;

    public bool HasLightingWork => _lightingEdits.Count > 0;

    public bool HasMeshWork => _dirtyMeshlets.Count > 0;

    public int LightingEditCount => _lightingEdits.Count;

    public int DirtyChunkCount => _dirtyMeshlets.Count;

    public void EnqueueVoxelEdit(
        VoxelWorld world,
        WorldVoxelCoord position)
    {
        ArgumentNullException.ThrowIfNull(world);

        _lightingEdits.Add(position);
        EnqueueVoxelMeshlets(world, position);
    }

    public void EnqueueVoxelMeshlets(
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

        _dirtyMeshlets[coord] =
            _dirtyMeshlets.TryGetValue(coord, out var existing)
                ? existing.Union(mask)
                : mask;
    }

    public WorldLightingBatch DrainLighting()
    {
        var lighting = _lightingEdits.ToArray();
        _lightingEdits.Clear();
        return new WorldLightingBatch(lighting);
    }

    public WorldMeshBatch DrainMeshlets()
    {
        var meshlets =
            new Dictionary<ChunkCoord, ChunkMeshletMask>(
                _dirtyMeshlets);
        _dirtyMeshlets.Clear();
        return new WorldMeshBatch(meshlets);
    }

    public void RequeueLighting(WorldLightingBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var position in batch.EditedPositions)
        {
            _lightingEdits.Add(position);
        }
    }

    public void RequeueMeshlets(WorldMeshBatch batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        foreach (var (coord, mask) in batch.DirtyMeshlets)
        {
            EnqueueMeshlets(coord, mask);
        }
    }
}

public sealed record WorldLightingBatch(
    IReadOnlyList<WorldVoxelCoord> EditedPositions)
{
    public bool IsEmpty => EditedPositions.Count == 0;
}

public sealed record WorldMeshBatch(
    IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> DirtyMeshlets)
{
    public bool IsEmpty => DirtyMeshlets.Count == 0;
}
