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

                        // Dense lighting updates can touch every meshlet
                        // thousands of times. A saturated mask needs no
                        // further per-voxel meshlet geometry checks.
                        var alreadyDirty =
                            dirty.TryGetValue(coord, out var existing);
                        if (alreadyDirty && existing.IsAll)
                            return;

                        var mask =
                            ChunkMeshletMask.ForWorldPosition(
                                coord,
                                position);
                        if (mask.IsEmpty)
                            return;

                        dirty[coord] =
                            alreadyDirty ? existing.Union(mask) : mask;
                    });
        }

        return dirty;
    }
}
