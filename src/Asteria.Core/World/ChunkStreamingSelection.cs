namespace Asteria.Core.World;

public static class ChunkStreamingSelection
{
    private const int SurfacePaddingBelowChunks = 2;
    private const int SurfacePaddingAboveChunks = 1;
    private const int PlayerLocalHorizontalRadiusChunks = 2;
    private const int PlayerLocalVerticalRadiusChunks = 2;

    public static HashSet<ChunkCoord> DesiredSurfaceChunks(
        ChunkCoord center,
        int horizontalRadius,
        IChunkSurfaceRangeProvider surfaceRanges)
    {
        ArgumentNullException.ThrowIfNull(
            surfaceRanges);

        var radius =
            Math.Max(
                1,
                horizontalRadius);
        var radiusSquared =
            radius *
            radius;
        var desired =
            DesiredPlayerLocalChunks(
                center,
                horizontalRadius);

        for (var dz = -radius;
             dz <= radius;
             dz++)
        {
            for (var dx = -radius;
                 dx <= radius;
                 dx++)
            {
                if (dx * dx +
                        dz * dz >
                    radiusSquared)
                {
                    continue;
                }

                var chunkX =
                    checked(
                        center.X +
                        dx);
                var chunkZ =
                    checked(
                        center.Z +
                        dz);
                var surface =
                    surfaceRanges.GetSurfaceRange(
                        chunkX,
                        chunkZ);

                if (surface.MaximumWorldY <
                    surface.MinimumWorldY)
                {
                    throw new InvalidOperationException(
                        "Surface range maximum cannot be below its minimum.");
                }

                var minimumChunk =
                    Math.Max(
                        0,
                        WorldYToChunkY(
                            surface.MinimumWorldY) -
                        SurfacePaddingBelowChunks);
                var maximumChunk =
                    Math.Max(
                        minimumChunk,
                        WorldYToChunkY(
                            surface.MaximumWorldY) +
                        SurfacePaddingAboveChunks);

                InsertVerticalRange(
                    desired,
                    chunkX,
                    chunkZ,
                    minimumChunk,
                    maximumChunk);

            }
        }

        return desired;
    }

    public static HashSet<ChunkCoord> DesiredPlayerLocalChunks(
        ChunkCoord center,
        int horizontalRadius)
    {
        var radius =
            Math.Max(
                1,
                horizontalRadius);
        var radiusSquared =
            radius *
            radius;
        var desired =
            new HashSet<ChunkCoord>();
        var minimumLocal =
            Math.Max(
                0,
                center.Y -
                PlayerLocalVerticalRadiusChunks);
        var maximumLocal =
            Math.Max(
                minimumLocal,
                center.Y +
                PlayerLocalVerticalRadiusChunks);

        for (var dz = -radius;
             dz <= radius;
             dz++)
        {
            for (var dx = -radius;
                 dx <= radius;
                 dx++)
            {
                if (dx * dx +
                        dz * dz >
                    radiusSquared ||
                    Math.Abs(dx) >
                        PlayerLocalHorizontalRadiusChunks ||
                    Math.Abs(dz) >
                        PlayerLocalHorizontalRadiusChunks)
                {
                    continue;
                }

                InsertVerticalRange(
                    desired,
                    checked(center.X + dx),
                    checked(center.Z + dz),
                    minimumLocal,
                    maximumLocal);
            }
        }

        return desired;
    }

    private static int WorldYToChunkY(
        int worldY) =>
        VoxelCoordinates.FromWorld(
                0,
                worldY,
                0)
            .Chunk.Y;

    private static void InsertVerticalRange(
        ISet<ChunkCoord> desired,
        int chunkX,
        int chunkZ,
        int minimumChunkY,
        int maximumChunkY)
    {
        for (var chunkY = minimumChunkY;
             chunkY <= maximumChunkY;
             chunkY++)
        {
            desired.Add(
                new ChunkCoord(
                    chunkX,
                    chunkY,
                    chunkZ));
        }
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
