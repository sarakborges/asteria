namespace Asteria.Core.World;

/// <summary>
/// Bounded memoization of immutable surface columns. The cache is an
/// acceleration detail, never a generated-world ownership boundary.
/// Lazy materialization avoids duplicate concurrent requests for the same
/// column without holding the global cache lock during expensive sampling.
/// </summary>
public sealed class SurfaceTerrainColumnCache
{
    private readonly SurfaceTerrainField _terrain;
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<
        (int X, int Z),
        LinkedListNode<CachedColumn>> _entries = [];
    private readonly LinkedList<CachedColumn> _recent = new();

    public SurfaceTerrainColumnCache(
        SurfaceTerrainField terrain,
        int capacity = 128)
    {
        _terrain = terrain ??
            throw new ArgumentNullException(nameof(terrain));
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public int CachedColumnCount
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public SurfaceTerrainColumn Get(int chunkX, int chunkZ)
    {
        Lazy<SurfaceTerrainColumn> sample;
        var key = (X: chunkX, Z: chunkZ);

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                _recent.Remove(cached);
                _recent.AddFirst(cached);
                sample = cached.Value.Sample;
            }
            else
            {
                sample = new Lazy<SurfaceTerrainColumn>(
                    () => _terrain.SampleColumn(chunkX, chunkZ),
                    LazyThreadSafetyMode.ExecutionAndPublication);
                var entry = _recent.AddFirst(
                    new CachedColumn(key, sample));
                _entries.Add(key, entry);

                if (_entries.Count > _capacity)
                {
                    var eldest = _recent.Last!;
                    _entries.Remove(eldest.Value.Key);
                    _recent.RemoveLast();
                }
            }
        }

        return sample.Value;
    }

    private sealed record CachedColumn(
        (int X, int Z) Key,
        Lazy<SurfaceTerrainColumn> Sample);
}
