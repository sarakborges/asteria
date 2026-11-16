namespace Asteria.Core.World;

public readonly record struct ChunkMeshletKey(
    ChunkCoord Chunk,
    int MeshletIndex);

public sealed class MeshletContentRevisions
{
    private readonly Dictionary<ChunkCoord, ulong[]>
        _revisions = [];

    public ulong Get(ChunkMeshletKey key)
    {
        ValidateMeshletIndex(
            key.MeshletIndex);

        return _revisions.TryGetValue(
                key.Chunk,
                out var revisions)
            ? revisions[key.MeshletIndex]
            : 0;
    }

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
        if (mask.IsEmpty)
        {
            return;
        }

        if (!_revisions.TryGetValue(
                coord,
                out var revisions))
        {
            revisions =
                new ulong[
                    ChunkMeshletMask.Count];
            _revisions.Add(
                coord,
                revisions);
        }

        for (var meshletIndex = 0;
             meshletIndex < ChunkMeshletMask.Count;
             meshletIndex++)
        {
            if (!mask.ContainsIndex(
                    meshletIndex))
            {
                continue;
            }

            revisions[meshletIndex] =
                checked(
                    revisions[meshletIndex] +
                    1);
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
                captured.Add(
                    key,
                    Get(key));
            }
        }

        return captured;
    }

    public bool IsCurrent(
        ChunkMeshletKey key,
        ulong revision) =>
        Get(key) == revision;

    public void RemoveChunk(
        ChunkCoord coord) =>
        _revisions.Remove(coord);

    private static void ValidateMeshletIndex(
        int meshletIndex)
    {
        if ((uint)meshletIndex >=
            ChunkMeshletMask.Count)
        {
            throw new ArgumentOutOfRangeException(
                nameof(meshletIndex));
        }
    }
}
