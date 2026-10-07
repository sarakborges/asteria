namespace Asteria.Core.World;

/// <summary>
/// Thread-safe bounded memoization for immutable deterministic calculations.
/// The cache is not authoritative: capacity, eviction and concurrency may
/// change recomputation, never the generated result.
/// </summary>
internal sealed class BoundedMemoCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly object _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<CachedEntry>> _entries = [];
    private readonly LinkedList<CachedEntry> _recent = new();

    public BoundedMemoCache(int capacity)
    {
        if (capacity < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }

        _capacity = capacity;
    }

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _entries.Count;
            }
        }
    }

    public TValue GetOrAdd(TKey key, Func<TValue> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        Lazy<TValue> lazy;

        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var node))
            {
                _recent.Remove(node);
                _recent.AddFirst(node);
                lazy = node.Value.Value;
            }
            else
            {
                lazy = new Lazy<TValue>(
                    factory,
                    LazyThreadSafetyMode.ExecutionAndPublication);
                var entry = _recent.AddFirst(new CachedEntry(key, lazy));
                _entries.Add(key, entry);

                if (_entries.Count > _capacity)
                {
                    var eldest = _recent.Last!;
                    _entries.Remove(eldest.Value.Key);
                    _recent.RemoveLast();
                }
            }
        }

        // Never evaluate a heavy query while holding the cache lock.
        return lazy.Value;
    }

    private sealed record CachedEntry(
        TKey Key,
        Lazy<TValue> Value);
}
