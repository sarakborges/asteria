namespace Asteria.Core.World;

public sealed class DeduplicatedQueue<T>
    where T : notnull
{
    private readonly LinkedList<T> _order = new();
    private readonly Dictionary<T, LinkedListNode<T>> _nodes = [];

    public int Count => _nodes.Count;

    public ulong Revision { get; private set; }

    public bool Contains(T value) =>
        _nodes.ContainsKey(value);

    public bool Enqueue(T value)
    {
        if (_nodes.ContainsKey(value))
        {
            return false;
        }

        var node = _order.AddLast(value);
        _nodes.Add(value, node);
        BumpRevision();
        return true;
    }

    public bool EnqueueFront(T value)
    {
        if (_nodes.TryGetValue(value, out var existing))
        {
            if (ReferenceEquals(_order.First, existing))
            {
                return false;
            }

            _order.Remove(existing);
            _order.AddFirst(existing);
            BumpRevision();
            return true;
        }

        var node = _order.AddFirst(value);
        _nodes.Add(value, node);
        BumpRevision();
        return true;
    }

    public bool Remove(T value)
    {
        if (!_nodes.Remove(value, out var node))
        {
            return false;
        }

        _order.Remove(node);
        BumpRevision();
        return true;
    }

    public bool TryDequeue(out T value)
    {
        var first = _order.First;

        if (first is null)
        {
            value = default!;
            return false;
        }

        value = first.Value;
        _order.RemoveFirst();
        _nodes.Remove(value);
        BumpRevision();
        return true;
    }

    public IReadOnlyList<T> Drain() =>
        Drain(int.MaxValue);

    public IReadOnlyList<T> Drain(
        int maximumItems)
    {
        if (maximumItems <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumItems));
        }

        if (_order.Count == 0)
        {
            return Array.Empty<T>();
        }

        var count =
            Math.Min(
                maximumItems,
                _order.Count);
        var result =
            new T[count];

        for (var index = 0;
             index < count;
             index++)
        {
            var node =
                _order.First ??
                throw new InvalidOperationException(
                    "Deduplicated queue order and index are inconsistent.");
            var value =
                node.Value;

            result[index] =
                value;
            _order.RemoveFirst();
            _nodes.Remove(
                value);
        }

        BumpRevision();
        return result;
    }

    /// <summary>
    /// Removes the longest FIFO prefix accepted by the predicate, stopping
    /// before the first rejected item. The remaining queue keeps its order.
    /// </summary>
    public IReadOnlyList<T> DrainPrefix(
        int maximumItems,
        Func<T, bool> accept)
    {
        if (maximumItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumItems));
        ArgumentNullException.ThrowIfNull(accept);

        var result = new List<T>(Math.Min(maximumItems, _nodes.Count));
        while (result.Count < maximumItems &&
               _order.First is { } first &&
               accept(first.Value))
        {
            var value = first.Value;
            _order.RemoveFirst();
            _nodes.Remove(value);
            result.Add(value);
        }

        if (result.Count > 0)
            BumpRevision();

        return result;
    }

    public IReadOnlyList<T> ValuesInOrder() =>
        _order.ToArray();

    public void Clear()
    {
        if (_order.Count == 0)
        {
            return;
        }

        _order.Clear();
        _nodes.Clear();
        BumpRevision();
    }

    private void BumpRevision()
    {
        Revision =
            Revision == ulong.MaxValue
                ? 1
                : Revision + 1;
    }
}
