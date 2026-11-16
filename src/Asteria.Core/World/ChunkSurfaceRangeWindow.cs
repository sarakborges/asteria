namespace Asteria.Core.World;

public sealed class ChunkSurfaceRangeWindow :
    IChunkSurfaceRangeProvider
{
    private readonly IChunkSurfaceRangeProvider _source;
    private readonly Dictionary<(int X, int Z), ChunkSurfaceRange>
        _ranges = [];

    public ChunkSurfaceRangeWindow(
        IChunkSurfaceRangeProvider source)
    {
        _source =
            source ??
            throw new ArgumentNullException(
                nameof(source));
    }

    public int CachedColumnCount =>
        _ranges.Count;

    public void RetainWindow(
        int centerX,
        int centerZ,
        int horizontalRadius)
    {
        if (horizontalRadius < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horizontalRadius));
        }

        var radius =
            (long)horizontalRadius;

        foreach (var key in
                 _ranges.Keys.ToArray())
        {
            if (Math.Abs(
                    (long)key.X -
                    centerX) <=
                    radius &&
                Math.Abs(
                    (long)key.Z -
                    centerZ) <=
                    radius)
            {
                continue;
            }

            _ranges.Remove(
                key);
        }
    }

    public ChunkSurfaceRange GetSurfaceRange(
        int chunkX,
        int chunkZ)
    {
        var key =
            (
                X: chunkX,
                Z: chunkZ
            );

        if (_ranges.TryGetValue(
                key,
                out var cached))
        {
            return cached;
        }

        var range =
            _source.GetSurfaceRange(
                chunkX,
                chunkZ);
        _ranges.Add(
            key,
            range);
        return range;
    }
}
