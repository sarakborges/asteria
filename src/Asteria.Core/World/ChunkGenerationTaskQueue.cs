namespace Asteria.Core.World;

/// <summary>
/// Identity of one logical generation request. Several vertical sections may
/// be coordinated by the same horizontal column task; the target step keeps
/// requests for different generation statuses distinct.
/// </summary>
public readonly record struct ChunkGenerationTaskKey
{
    public ChunkGenerationTaskKey(
        ChunkColumnCoord column,
        ChunkGenerationStepId targetStep)
    {
        if (!Enum.IsDefined(targetStep))
            throw new ArgumentOutOfRangeException(nameof(targetStep));

        Column = column;
        TargetStep = targetStep;
    }

    public ChunkColumnCoord Column { get; }

    public ChunkGenerationStepId TargetStep { get; }
}

/// <summary>
/// Lower ticket levels are more urgent. Horizontal distance is a deterministic
/// secondary key; the dispatcher may derive a new value when player tickets
/// or movement change.
/// </summary>
public readonly record struct ChunkGenerationTaskPriority(
    int TicketLevel,
    long HorizontalDistanceSquared) : IComparable<ChunkGenerationTaskPriority>
{
    public int CompareTo(ChunkGenerationTaskPriority other)
    {
        var result = TicketLevel.CompareTo(other.TicketLevel);
        return result != 0
            ? result
            : HorizontalDistanceSquared.CompareTo(
                other.HorizontalDistanceSquared);
    }
}

/// <summary>
/// A queued request together with its original enqueue age and stable FIFO
/// sequence. Reprioritizing a request must preserve both values.
/// </summary>
public readonly record struct ChunkGenerationQueuedTask(
    ChunkGenerationTaskKey Key,
    ChunkGenerationTaskPriority Priority,
    long EnqueuedTimestamp,
    ulong EnqueueSequence);

/// <summary>
/// Deterministic, deduplicated priority queue owned by the serial generation
/// dispatcher. It is deliberately not thread-safe. Priority updates reinsert
/// queued entries while preserving their original age and FIFO sequence.
/// </summary>
public sealed class ChunkGenerationTaskQueue
{
    private readonly Dictionary<ChunkGenerationTaskKey, Entry> _entries = [];
    private readonly SortedSet<Entry> _ordered = new(EntryComparer.Instance);
    private ulong _nextSequence = 1;

    public int Count => _entries.Count;

    public bool IsEmpty => _entries.Count == 0;

    public bool Contains(ChunkGenerationTaskKey key) =>
        _entries.ContainsKey(key);

    /// <summary>
    /// Adds a request once. A duplicate enqueue leaves the original priority,
    /// enqueue timestamp and stable sequence unchanged.
    /// </summary>
    public bool Enqueue(
        ChunkGenerationTaskKey key,
        ChunkGenerationTaskPriority priority,
        long enqueuedTimestamp)
    {
        if (_entries.ContainsKey(key))
            return false;
        if (_nextSequence == ulong.MaxValue)
            throw new InvalidOperationException(
                "Generation queue enqueue sequence was exhausted.");

        var entry = new Entry(
            key,
            priority,
            enqueuedTimestamp,
            _nextSequence++);
        if (!_ordered.Add(entry))
            throw new InvalidOperationException(
                "Generation queue rejected a unique entry.");

        _entries.Add(key, entry);
        return true;
    }

    /// <summary>
    /// Changes only the priority. Original enqueue age and tie-breaking order
    /// survive movement/ticket updates.
    /// </summary>
    public bool UpdatePriority(
        ChunkGenerationTaskKey key,
        ChunkGenerationTaskPriority priority)
    {
        if (!_entries.TryGetValue(key, out var current))
            return false;

        var updated = current with { Priority = priority };
        if (!_ordered.Remove(current) || !_ordered.Add(updated))
            throw new InvalidOperationException(
                $"Could not requeue generation task after priority update: {key}");

        _entries[key] = updated;
        return true;
    }

    public bool TryGet(
        ChunkGenerationTaskKey key,
        out ChunkGenerationQueuedTask task)
    {
        if (_entries.TryGetValue(key, out var entry))
        {
            task = entry.ToPublic();
            return true;
        }

        task = default;
        return false;
    }

    public bool TryPeek(out ChunkGenerationQueuedTask task)
    {
        if (_ordered.Count == 0)
        {
            task = default;
            return false;
        }

        task = _ordered.Min.ToPublic();
        return true;
    }

    public bool TryDequeue(out ChunkGenerationQueuedTask task)
    {
        if (_ordered.Count == 0)
        {
            task = default;
            return false;
        }

        var entry = _ordered.Min;
        if (!_ordered.Remove(entry) || !_entries.Remove(entry.Key))
            throw new InvalidOperationException(
                $"Generation queue indexes diverged for {entry.Key}");

        task = entry.ToPublic();
        return true;
    }

    public bool Remove(ChunkGenerationTaskKey key)
    {
        if (!_entries.Remove(key, out var entry))
            return false;
        if (!_ordered.Remove(entry))
            throw new InvalidOperationException(
                $"Generation queue indexes diverged for {key}");
        return true;
    }

    public void Clear()
    {
        _entries.Clear();
        _ordered.Clear();
    }

    private readonly record struct Entry(
        ChunkGenerationTaskKey Key,
        ChunkGenerationTaskPriority Priority,
        long EnqueuedTimestamp,
        ulong EnqueueSequence)
    {
        public ChunkGenerationQueuedTask ToPublic() =>
            new(Key, Priority, EnqueuedTimestamp, EnqueueSequence);
    }

    private sealed class EntryComparer : IComparer<Entry>
    {
        public static EntryComparer Instance { get; } = new();

        public int Compare(Entry left, Entry right)
        {
            var result = left.Priority.CompareTo(right.Priority);
            if (result != 0)
                return result;

            result = left.EnqueueSequence.CompareTo(right.EnqueueSequence);
            if (result != 0)
                return result;

            result = left.Key.Column.CompareTo(right.Key.Column);
            return result != 0
                ? result
                : left.Key.TargetStep.CompareTo(right.Key.TargetStep);
        }
    }
}
