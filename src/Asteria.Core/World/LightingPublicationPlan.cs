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
        IReadOnlyList<WorldVoxelCoord> changedPositions,
        IEnumerable<WorldVoxelCoord>? priorityPositions = null)
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
        if (priorityPositions is null || result.Count < 2)
            return result;

        var priorityChunks = priorityPositions
            .Select(position => VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z).Chunk)
            .Distinct()
            .OrderBy(coord => coord.Y)
            .ThenBy(coord => coord.Z)
            .ThenBy(coord => coord.X)
            .ToArray();
        if (priorityChunks.Length == 0)
            return result;

        // Publish around the triggering edit first. A large propagation
        // result may span many chunks, but the edited area should not wait
        // for the lexicographically first distant chunk to be copied.
        return result
            .Select(publication => (
                Publication: publication,
                Distance: NearestSeedDistance(publication.Coord, priorityChunks)))
            .OrderBy(entry => entry.Distance)
            .ThenBy(entry => entry.Publication.Coord.Y)
            .ThenBy(entry => entry.Publication.Coord.Z)
            .ThenBy(entry => entry.Publication.Coord.X)
            .Select(entry => entry.Publication)
            .ToArray();
    }

    private static long NearestSeedDistance(
        ChunkCoord coord,
        IReadOnlyList<ChunkCoord> seeds)
    {
        var nearest = long.MaxValue;
        foreach (var seed in seeds)
        {
            var distance =
                Math.Abs((long)coord.X - seed.X) +
                Math.Abs((long)coord.Y - seed.Y) +
                Math.Abs((long)coord.Z - seed.Z);
            nearest = Math.Min(nearest, distance);
        }
        return nearest;
    }
}
