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
/// Worker-to-coordinator message. Result must be a detached immutable payload;
/// Error is populated instead when the worker operation failed. Workers may
/// publish this value but must never mutate task lifecycle or dispatcher state.
/// </summary>
public readonly record struct ChunkGenerationDependencyCompletion(
    ChunkGenerationTaskInstance Task,
    ulong WaitToken,
    ulong DependencyId,
    object? Result = null,
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
/// Thread-safe completion mailbox. Workers publish detached results without
/// blocking; the serial coordinator is the only consumer. The queue is
/// demand-driven and does not impose a worker-count-derived completion cap.
/// </summary>
public sealed class ChunkGenerationCompletionMailbox
{
    private readonly ConcurrentQueue<ChunkGenerationDependencyCompletion> _queue = new();
    private int _queuedCount;

    public int PendingCount => Volatile.Read(ref _queuedCount);

    public bool TryDequeue(out ChunkGenerationDependencyCompletion completion)
    {
        if (!_queue.TryDequeue(out completion))
            return false;

        Interlocked.Decrement(ref _queuedCount);
        return true;
    }

    public void Publish(ChunkGenerationDependencyCompletion completion)
    {
        completion.Validate();
        // Publish the count before enqueue so concurrent diagnostics do not
        // momentarily report a negative count when the consumer dequeues.
        Interlocked.Increment(ref _queuedCount);
        _queue.Enqueue(completion);
    }
}
