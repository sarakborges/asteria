namespace Asteria.Core.World;

public static class MeshletInvalidation
{
    public static IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask>
        ForPositions(
            VoxelWorld world,
            IEnumerable<WorldVoxelCoord> positions)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(positions);

        var dirty =
            new Dictionary<ChunkCoord, ChunkMeshletMask>();

        foreach (var position in positions)
        {
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

                        if (mask.IsEmpty)
                        {
                            return;
                        }

                        dirty[coord] =
                            dirty.TryGetValue(
                                coord,
                                out var existing)
                                ? existing.Union(mask)
                                : mask;
                    });
        }

        return dirty;
    }
}
