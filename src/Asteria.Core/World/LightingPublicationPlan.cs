namespace Asteria.Core.World;

/// <summary>
/// Immutable worker-prepared light-publication unit. One complete chunk is
/// copied on the main thread. Its halo refreshes are coalesced and filtered
/// against live residency when flushed.
/// </summary>
public sealed record LightingPublicationChunk(
    ChunkCoord Coord,
    int ChangedVoxelCount,
    IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> DirtyMeshlets);

public static class LightingPublicationPlan
{
    /// <summary>
    /// The solver returns positions grouped in Y/Z/X chunk order. Reject
    /// unordered input rather than publishing a chunk twice.
    /// </summary>
    public static IReadOnlyList<LightingPublicationChunk> Create(
        IReadOnlyList<WorldVoxelCoord> changedPositions)
    {
        ArgumentNullException.ThrowIfNull(changedPositions);
        var result = new List<LightingPublicationChunk>();
        var offset = 0;
        while (offset < changedPositions.Count)
        {
            var first = changedPositions[offset];
            var coord = VoxelCoordinates.FromWorld(
                first.X, first.Y, first.Z).Chunk;
            if (result.Count > 0 &&
                ChunkCoordOrdering.YThenZThenX.Compare(
                    result[^1].Coord, coord) >= 0)
                throw new ArgumentException(
                    "Lighting positions must be grouped in chunk order.",
                    nameof(changedPositions));

            var end = offset + 1;
            while (end < changedPositions.Count)
            {
                var next = changedPositions[end];
                if (VoxelCoordinates.FromWorld(
                        next.X, next.Y, next.Z).Chunk != coord)
                    break;
                end++;
            }

            result.Add(new LightingPublicationChunk(
                coord, end - offset,
                MeshletInvalidation.ForPositionHalos(
                    changedPositions, offset, end - offset)));
            offset = end;
        }
        return result;
    }
}
