using System.Collections.Concurrent;
using System.Threading;

namespace Asteria.Core.World;

/// <summary>
/// Worker-to-coordinator result of one bounded task-advance continuation.
/// Independent task instances may publish completions concurrently.
/// </summary>
public readonly record struct ChunkGenerationTaskAdvanceCompletion(
    ChunkGenerationTaskInstance Task,
    ChunkGenerationTaskAdvanceResult? Result,
    Exception? Error)
{
    public void Validate()
    {
        Task.Validate();
        if ((Result is null) == (Error is null))
            throw new ArgumentException(
                "A task advance completion must contain exactly one result or error.");
    }
}

/// <summary>
/// Multi-producer mailbox for independent task advances. Only the owning
/// dispatcher consumes results and applies task lifecycle transitions.
/// </summary>
public sealed class ChunkGenerationTaskAdvanceMailbox
{
    private readonly ConcurrentQueue<ChunkGenerationTaskAdvanceCompletion> _queue = new();
    private int _pending;

    public int PendingCount => Volatile.Read(ref _pending);

    public void Publish(ChunkGenerationTaskAdvanceCompletion completion)
    {
        completion.Validate();
        Interlocked.Increment(ref _pending);
        _queue.Enqueue(completion);
    }

    public bool TryDequeue(out ChunkGenerationTaskAdvanceCompletion completion)
    {
        if (!_queue.TryDequeue(out completion))
            return false;

        Interlocked.Decrement(ref _pending);
        return true;
    }
}
