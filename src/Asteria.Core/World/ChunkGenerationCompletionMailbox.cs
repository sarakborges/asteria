using System.Collections.Concurrent;
using System.Threading;

namespace Asteria.Core.World;

/// <summary>
/// Identifies one specific execution of a logical generation task. Re-enqueueing
/// the same column/target after retirement must use a new generation value.
/// </summary>
public readonly record struct ChunkGenerationTaskInstance(
    ChunkGenerationTaskKey Key,
    ulong Generation)
{
    public void Validate()
    {
        if (Generation == 0)
            throw new ArgumentOutOfRangeException(
                nameof(Generation), "Task generation zero is reserved.");
    }
}

/// <summary>
/// Worker-to-coordinator message. Workers may publish this immutable value but
/// must never mutate the task lifecycle or dispatcher collections directly.
/// </summary>
public readonly record struct ChunkGenerationDependencyCompletion(
    ChunkGenerationTaskInstance Task,
    ulong WaitToken,
    ulong DependencyId,
    Exception? Error = null)
{
    public void Validate()
    {
        Task.Validate();
        if (WaitToken == 0)
            throw new ArgumentOutOfRangeException(
                nameof(WaitToken), "Wait token zero is reserved.");
        if (DependencyId == 0)
            throw new ArgumentOutOfRangeException(
                nameof(DependencyId), "Dependency ID zero is reserved.");
    }
}

/// <summary>
/// Bounded, thread-safe mailbox for worker completions. The coordinator is the
/// only consumer. Capacity is reserved before dispatching a worker operation,
/// so a completed worker can always publish without blocking or dropping its
/// completion. A reservation is released if dispatch fails before work starts.
/// </summary>
public sealed class ChunkGenerationCompletionMailbox
{
    private readonly ConcurrentQueue<ChunkGenerationDependencyCompletion> _queue = new();
    private readonly int _capacity;
    private int _occupiedSlots;
    private int _queuedCount;

    public ChunkGenerationCompletionMailbox(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Capacity => _capacity;

    public int PendingCount => Volatile.Read(ref _queuedCount);

    public int ReservedOrPendingCount => Volatile.Read(ref _occupiedSlots);

    public int AvailableCapacity =>
        _capacity - Volatile.Read(ref _occupiedSlots);

    /// <summary>
    /// Reserves one guaranteed completion slot before a worker task is launched.
    /// Returns false when all slots are either reserved or awaiting collection.
    /// </summary>
    public bool TryReserve(out ChunkGenerationCompletionReservation? reservation)
    {
        while (true)
        {
            var occupied = Volatile.Read(ref _occupiedSlots);
            if (occupied >= _capacity)
            {
                reservation = null;
                return false;
            }

            if (Interlocked.CompareExchange(
                    ref _occupiedSlots, occupied + 1, occupied) == occupied)
            {
                reservation = new ChunkGenerationCompletionReservation(this);
                return true;
            }
        }
    }

    /// <summary>
    /// Dequeues one completion for coordinator-side processing. Dequeueing
    /// releases the capacity slot, not worker publication.
    /// </summary>
    public bool TryDequeue(out ChunkGenerationDependencyCompletion completion)
    {
        if (!_queue.TryDequeue(out completion))
            return false;

        Interlocked.Decrement(ref _queuedCount);
        Interlocked.Decrement(ref _occupiedSlots);
        return true;
    }

    internal void PublishReserved(
        ChunkGenerationDependencyCompletion completion)
    {
        _queue.Enqueue(completion);
        Interlocked.Increment(ref _queuedCount);
    }

    internal void ReleaseReservation() =>
        Interlocked.Decrement(ref _occupiedSlots);
}

/// <summary>
/// One-shot capacity lease. Exactly one of Publish or Dispose releases ownership:
/// a published slot is released by the consumer after dequeue; an unused slot is
/// released by Dispose when dispatch could not start.
/// </summary>
public sealed class ChunkGenerationCompletionReservation : IDisposable
{
    private readonly ChunkGenerationCompletionMailbox _owner;
    private int _state;

    internal ChunkGenerationCompletionReservation(
        ChunkGenerationCompletionMailbox owner)
    {
        _owner = owner;
    }

    /// <summary>
    /// Publishes a completion without blocking. Returns false when this
    /// reservation has already been published or released.
    /// </summary>
    public bool TryPublish(ChunkGenerationDependencyCompletion completion)
    {
        completion.Validate();
        if (Interlocked.CompareExchange(ref _state, 1, 0) != 0)
            return false;

        _owner.PublishReserved(completion);
        return true;
    }

    /// <summary>Releases an unused reservation. No-op after successful publication.</summary>
    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _state, 2, 0) == 0)
            _owner.ReleaseReservation();
    }
}
