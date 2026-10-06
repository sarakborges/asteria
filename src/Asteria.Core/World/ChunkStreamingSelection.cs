namespace Asteria.Core.World;

public static class ChunkStreamingSelection
{
    public static HashSet<ChunkCoord> DesiredQaChunks(
        ChunkCoord center,
        int horizontalRadius,
        int minimumChunkY = 0,
        int maximumChunkY = 1)
    {
        var radius = Math.Max(1, horizontalRadius);
        var radiusSquared = radius * radius;
        var minimumY = Math.Min(
            minimumChunkY,
            maximumChunkY);
        var maximumY = Math.Max(
            minimumChunkY,
            maximumChunkY);
        var desired = new HashSet<ChunkCoord>();

        for (var dz = -radius; dz <= radius; dz++)
        {
            for (var dx = -radius; dx <= radius; dx++)
            {
                if (dx * dx + dz * dz > radiusSquared)
                {
                    continue;
                }

                for (var y = minimumY;
                     y <= maximumY;
                     y++)
                {
                    desired.Add(
                        new ChunkCoord(
                            center.X + dx,
                            y,
                            center.Z + dz));
                }
            }
        }

        return desired;
    }
}

public sealed class ChunkPresentationSelection
{
    public ChunkCoord? Center { get; private set; }

    public int ShowRadius { get; private set; }

    public int HideRadius { get; private set; }

    public ulong Revision { get; private set; }

    public void Sync(
        ChunkCoord center,
        int renderDistanceChunks)
    {
        var showRadius =
            Math.Max(1, renderDistanceChunks) + 1;
        var hideRadius = showRadius + 1;

        if (Center == center &&
            ShowRadius == showRadius &&
            HideRadius == hideRadius)
        {
            return;
        }

        Center = center;
        ShowRadius = showRadius;
        HideRadius = hideRadius;
        Revision++;
    }

    public bool RetainsRenderMesh(ChunkCoord coord) =>
        Center is { } center &&
        Inside(coord, center, HideRadius);

    public bool ShouldBeVisible(
        ChunkCoord coord,
        bool currentlyVisible)
    {
        if (Center is not { } center)
        {
            return false;
        }

        var threshold =
            currentlyVisible
                ? HideRadius
                : ShowRadius;

        return Inside(coord, center, threshold);
    }

    private static bool Inside(
        ChunkCoord coord,
        ChunkCoord center,
        int radius)
    {
        var dx = (long)coord.X - center.X;
        var dz = (long)coord.Z - center.Z;
        return dx * dx + dz * dz <=
               (long)radius * radius;
    }
}
