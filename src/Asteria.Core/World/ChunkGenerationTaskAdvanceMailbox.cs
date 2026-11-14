using System.Collections.Concurrent;
using System.Threading;

namespace Asteria.Core.World;

/// <summary>
/// Worker-to-coordinator result of one serial task-advance continuation.
/// Exactly one advance may be in flight per dimension, so this mailbox is
/// deliberately bounded to one published completion.
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
/// Single-slot mailbox for the dimension's serial worldgen executor. The
/// dispatcher does not schedule another task advance until it consumes the
/// previous completion, making a one-result bound an invariant rather than
/// a best-effort queue limit.
/// </summary>
public sealed class ChunkGenerationTaskAdvanceMailbox
{
    private readonly ConcurrentQueue<ChunkGenerationTaskAdvanceCompletion> _queue = new();
    private int _pending;

    public int PendingCount => Volatile.Read(ref _pending);

    public void Publish(ChunkGenerationTaskAdvanceCompletion completion)
    {
        completion.Validate();
        if (Interlocked.CompareExchange(ref _pending, 1, 0) != 0)
            throw new InvalidOperationException(
                "A dimension cannot publish a second task advance before the first is collected.");

        _queue.Enqueue(completion);
    }

    public bool TryDequeue(out ChunkGenerationTaskAdvanceCompletion completion)
    {
        if (!_queue.TryDequeue(out completion))
            return false;

        Volatile.Write(ref _pending, 0);
        return true;
    }
}
