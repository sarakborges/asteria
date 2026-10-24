namespace Asteria.Core.World;

internal static class MeshletMaskQueueDrain
{
    public static void Drain(
        SortedDictionary<ChunkCoord, ChunkMeshletMask> source,
        Dictionary<ChunkCoord, ChunkMeshletMask> destination,
        ref int remaining,
        int maximumChunks = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (maximumChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumChunks));

        if (remaining <= 0 || source.Count == 0)
            return;

        var startingChunkCount = destination.Count;
        while (remaining > 0 && source.Count > 0)
        {
            var enumerator = source.GetEnumerator();
            if (!enumerator.MoveNext())
                break;
            var coord = enumerator.Current.Key;
            if (destination.Count - startingChunkCount >= maximumChunks &&
                !destination.ContainsKey(coord))
                break;

            DrainCoordinate(source, destination, coord, ref remaining);
        }
    }

    public static void DrainNearest(
        SortedDictionary<ChunkCoord, ChunkMeshletMask> source,
        Dictionary<ChunkCoord, ChunkMeshletMask> destination,
        ref int remaining,
        ChunkCoord center,
        ChunkMovementDirection movement,
        int maximumChunks = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);
        if (maximumChunks <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumChunks));
        if (remaining <= 0 || source.Count == 0)
            return;

        var startingChunkCount = destination.Count;
        // A worker accepts a bounded set of meshlets and source chunks. Select
        // the closest eligible chunk directly instead of sorting/allocating
        // the entire backlog on the main thread for each worker start.
        while (remaining > 0 && source.Count > 0)
        {
            ChunkCoord? selected = null;
            ChunkLoadPriority selectedPriority = default;
            foreach (var coord in source.Keys)
            {
                var priority = ChunkStreamingState.Priority(
                    coord, center, movement);
                if (selected is null || priority.CompareTo(selectedPriority) < 0)
                {
                    selected = coord;
                    selectedPriority = priority;
                }
            }

            if (selected is not { } closest)
                break;
            if (destination.Count - startingChunkCount >= maximumChunks &&
                !destination.ContainsKey(closest))
                break;
            DrainCoordinate(source, destination, closest, ref remaining);
        }
    }

    private static void DrainCoordinate(
        SortedDictionary<ChunkCoord, ChunkMeshletMask> source,
        Dictionary<ChunkCoord, ChunkMeshletMask> destination,
        ChunkCoord coord,
        ref int remaining)
    {
        var pending = source[coord];
        var selected = ChunkMeshletMask.None;
        for (var index = 0; index < ChunkMeshletMask.Count && remaining > 0; index++)
        {
            if (!pending.ContainsIndex(index))
                continue;
            selected = selected.Union(ChunkMeshletMask.Single(index));
            remaining--;
        }

        destination[coord] = destination.TryGetValue(coord, out var existing)
            ? existing.Union(selected)
            : selected;
        var rest = pending.Except(selected);
        if (rest.IsEmpty)
            source.Remove(coord);
        else
            source[coord] = rest;
    }
}
