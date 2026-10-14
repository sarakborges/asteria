namespace Asteria.Core.World;

public static class MeshletInvalidation
{
    public static IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask>
        ForPositions(VoxelWorld world, IEnumerable<WorldVoxelCoord> positions)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(positions);

        var dirty = new Dictionary<ChunkCoord, ChunkMeshletMask>();
        foreach (var position in positions)
            Accumulate(world, position, dirty);
        return dirty;
    }

    // Bounded lighting publication slices already have an indexed input.
    // Do not copy each slice just to enumerate its halo invalidations.
    public static IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask>
        ForPositions(
            VoxelWorld world,
            IReadOnlyList<WorldVoxelCoord> positions,
            int offset,
            int count)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(positions);
        if (offset < 0 || count < 0 || offset > positions.Count - count)
            throw new ArgumentOutOfRangeException(nameof(offset));

        var dirty = new Dictionary<ChunkCoord, ChunkMeshletMask>();
        for (var index = offset; index < offset + count; index++)
            Accumulate(world, positions[index], dirty);
        return dirty;
    }

    private static void Accumulate(
        VoxelWorld world, WorldVoxelCoord position,
        Dictionary<ChunkCoord, ChunkMeshletMask> dirty)
    {
        VoxelCoordinates.VisitChunkCoordsWhoseVoxelHaloContains(
            position,
            coord =>
            {
                if (!world.ContainsChunk(coord))
                    return;

                // Once a chunk mask is saturated, further voxel checks
                // cannot contribute another dirty meshlet.
                var alreadyDirty = dirty.TryGetValue(coord, out var existing);
                if (alreadyDirty && existing.IsAll)
                    return;

                var mask = ChunkMeshletMask.ForWorldPosition(coord, position);
                if (mask.IsEmpty)
                    return;
                dirty[coord] = alreadyDirty ? existing.Union(mask) : mask;
            });
    }
}
