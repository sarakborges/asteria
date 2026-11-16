namespace Asteria.Core.World;

/// <summary>
/// Single-owner, bounded FIFO cache for resources whose live owner can
/// temporarily release them. Removing an item transfers ownership back to
/// the caller; eviction and clearing explicitly dispose the displaced item.
/// Retired presentations are never counted as active streaming residency.
/// </summary>
public sealed class BoundedReuseCache<TKey, TValue>
    where TKey : notnull
    where TValue : class
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>>
        _entries = [];
    private readonly LinkedList<(TKey Key, TValue Value)> _order = [];

    public BoundedReuseCache(int capacity)
    {
        if (capacity < 1)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count => _entries.Count;

    public bool TryTake(TKey key, out TValue? value)
    {
        if (!_entries.Remove(key, out var node))
        {
            value = null;
            return false;
        }

        _order.Remove(node);
        value = node.Value.Value;
        return true;
    }

    public void Store(TKey key, TValue value, Action<TValue> disposeEvicted)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(disposeEvicted);
        if (_entries.ContainsKey(key))
            throw new InvalidOperationException("Resource is already cached.");

        var node = _order.AddLast((key, value));
        _entries.Add(key, node);
        if (_entries.Count <= _capacity)
            return;

        var oldest = _order.First!;
        _order.RemoveFirst();
        _entries.Remove(oldest.Value.Key);
        disposeEvicted(oldest.Value.Value);
    }

    public void Clear(Action<TValue> dispose)
    {
        ArgumentNullException.ThrowIfNull(dispose);
        foreach (var (_, value) in _order)
            dispose(value);
        _entries.Clear();
        _order.Clear();
    }
}
