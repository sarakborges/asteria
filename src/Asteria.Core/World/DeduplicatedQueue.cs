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

    public IReadOnlyList<T> Drain()
    {
        if (_order.Count == 0)
        {
            return Array.Empty<T>();
        }

        var result = _order.ToArray();
        _order.Clear();
        _nodes.Clear();
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
