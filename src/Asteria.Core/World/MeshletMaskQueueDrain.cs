namespace Asteria.Core.World;

internal static class MeshletMaskQueueDrain
{
    public static void Drain(
        Dictionary<ChunkCoord, ChunkMeshletMask> source,
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

        foreach (var coord in
                 source.Keys
                     .OrderBy(coord => coord.Y)
                     .ThenBy(coord => coord.Z)
                     .ThenBy(coord => coord.X)
                     .ToArray())
        {
            if (remaining <= 0)
            {
                break;
            }

            var pending =
                source[coord];
            var selected =
                ChunkMeshletMask.None;

            foreach (var meshletIndex in
                     pending.Indices())
            {
                if (remaining <= 0)
                {
                    break;
                }

                selected =
                    selected.Union(
                        ChunkMeshletMask.Single(
                            meshletIndex));
                remaining--;
            }

            if (selected.IsEmpty)
            {
                continue;
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
