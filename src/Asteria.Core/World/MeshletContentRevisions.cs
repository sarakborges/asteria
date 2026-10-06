namespace Asteria.Core.World;

public readonly record struct ChunkMeshletKey(
    ChunkCoord Chunk,
    int MeshletIndex);

public sealed class MeshletContentRevisions
{
    private readonly Dictionary<ChunkMeshletKey, ulong> _revisions = [];

    public ulong Get(ChunkMeshletKey key) =>
        _revisions.TryGetValue(key, out var revision)
            ? revision
            : 0;

    public void BumpVoxelEdit(
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

                Bump(
                    coord,
                    ChunkMeshletMask.ForWorldPosition(
                        coord,
                        position));
            });
    }

    public void Bump(
        ChunkCoord coord,
        ChunkMeshletMask mask)
    {
        foreach (var meshletIndex in mask.Indices())
        {
            var key =
                new ChunkMeshletKey(
                    coord,
                    meshletIndex);
            _revisions[key] = checked(Get(key) + 1);
        }
    }

    public IReadOnlyDictionary<ChunkMeshletKey, ulong> Capture(
        IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> meshlets)
    {
        ArgumentNullException.ThrowIfNull(meshlets);

        var captured =
            new Dictionary<ChunkMeshletKey, ulong>();

        foreach (var (coord, mask) in meshlets)
        {
            foreach (var meshletIndex in mask.Indices())
            {
                var key =
                    new ChunkMeshletKey(
                        coord,
                        meshletIndex);
                captured.Add(key, Get(key));
            }
        }

        return captured;
    }

    public bool IsCurrent(
        ChunkMeshletKey key,
        ulong revision) =>
        Get(key) == revision;
}
