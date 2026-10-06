namespace Asteria.Core.World;

internal static class MeshletMaskQueueDrain
{
    public static void Drain(
        SortedDictionary<ChunkCoord, ChunkMeshletMask> source,
        Dictionary<ChunkCoord, ChunkMeshletMask> destination,
        ref int remaining)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(destination);

        if (remaining <= 0 ||
            source.Count == 0)
        {
            return;
        }

        while (remaining > 0 &&
               source.Count > 0)
        {
            var enumerator =
                source.GetEnumerator();

            if (!enumerator.MoveNext())
            {
                break;
            }

            var (coord, pending) =
                enumerator.Current;
            var selected =
                ChunkMeshletMask.None;

            for (var meshletIndex = 0;
                 meshletIndex < ChunkMeshletMask.Count &&
                 remaining > 0;
                 meshletIndex++)
            {
                if (!pending.ContainsIndex(
                        meshletIndex))
                {
                    continue;
                }

                selected =
                    selected.Union(
                        ChunkMeshletMask.Single(
                            meshletIndex));
                remaining--;
            }

            destination[coord] =
                destination.TryGetValue(
                    coord,
                    out var existing)
                    ? existing.Union(selected)
                    : selected;

            var rest =
                pending.Except(selected);

            if (rest.IsEmpty)
            {
                source.Remove(coord);
            }
            else
            {
                source[coord] = rest;
            }
        }
    }
}
