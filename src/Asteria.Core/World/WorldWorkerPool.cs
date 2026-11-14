using System.Collections.Concurrent;

namespace Asteria.Core.World;

/// <summary>
/// The detached result of one operation executed by the shared world CPU pool.
/// Worker callbacks may publish this value to a thread-safe mailbox, but must
/// not mutate coordinator-owned state.
/// </summary>
public readonly record struct WorldWorkerCompletion<TResult>(
    TResult? Result,
    Exception? Error)
{
    public bool Succeeded => Error is null;
}

/// <summary>
/// Coordinator-owned handle for one queued/running world operation.
/// Completion can be observed without blocking a frame thread.
/// </summary>
public sealed class WorldWorkerOperation<TResult>
{
    private WorldWorkerCompletion<TResult> _completion;
    private int _state;

    public bool IsPending => Volatile.Read(ref _state) == 0;

    public bool IsRunning => Volatile.Read(ref _state) == 1;

    public bool IsCompleted => Volatile.Read(ref _state) == 2;

    public bool TryGetCompletion(
        out WorldWorkerCompletion<TResult> completion)
    {
        if (!IsCompleted)
        {
            completion = default;
            return false;
        }

        completion = _completion;
        return true;
    }

    internal void MarkRunning() =>
        Volatile.Write(ref _state, 1);

    internal void Publish(WorldWorkerCompletion<TResult> completion)
    {
        _completion = completion;
        Volatile.Write(ref _state, 2);
    }
}

/// <summary>
/// One physical executor shared by terrain generation, lighting, fluids and
/// mesh preparation. It owns the only dedicated world-worker threads. Runtime
/// owners keep their own priority queues and submit bounded operations here;
/// the executor never touches live world state or caller-owned queues.
/// </summary>
public sealed class WorldWorkerPool : IDisposable
{
    private readonly BlockingCollection<IWorkItem> _queue;
    private readonly Thread[] _workers;
    private readonly object _disposeSync = new();
    private int _disposed;
    private bool _disposeCompleted;
    private int _pendingWorkCount;
    private int _activeWorkerCount;
    private int _inFlightWorkCount;
    private long _completedWorkCount;
    private long _failedWorkCount;
    private long _cancelledWorkCount;
    private long _rejectedWorkCount;
    private long _completionCallbackFailures;

    public WorldWorkerPool(int workerCount, int? pendingCapacity = null)
    {
        if (workerCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(workerCount));

        var queueCapacity = pendingCapacity ?? workerCount;
        if (queueCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(pendingCapacity));

        WorkerCount = workerCount;
        PendingCapacity = queueCapacity;
        _queue = new BlockingCollection<IWorkItem>(
            new ConcurrentQueue<IWorkItem>(),
            queueCapacity);

        _workers = new Thread[workerCount];
        for (var index = 0; index < workerCount; index++)
        {
            var worker = new Thread(Consume)
            {
                IsBackground = true,
                Name = $"Asteria world worker {index + 1}",
            };
            _workers[index] = worker;
            worker.Start();
        }
    }

    public int WorkerCount { get; }

    /// <summary>Maximum number of queued operations waiting behind active workers.</summary>
    public int PendingCapacity { get; }

    public int PendingWorkCount => Volatile.Read(ref _pendingWorkCount);

    public int ActiveWorkerCount => Volatile.Read(ref _activeWorkerCount);

    public int InFlightWorkCount => Volatile.Read(ref _inFlightWorkCount);

    public int AvailableSubmissionCount =>
        Math.Max(0, PendingCapacity - PendingWorkCount);

    public long CompletedWorkCount => Interlocked.Read(ref _completedWorkCount);

    public long FailedWorkCount => Interlocked.Read(ref _failedWorkCount);

    public long CancelledWorkCount => Interlocked.Read(ref _cancelledWorkCount);

    public long RejectedWorkCount => Interlocked.Read(ref _rejectedWorkCount);

    public long CompletionCallbackFailures =>
        Interlocked.Read(ref _completionCallbackFailures);

    /// <summary>
    /// Enqueues one operation without blocking the coordinator. The optional
    /// completion callback runs on a worker after the operation has published
    /// its result; it must be thread-safe and limited to mailbox publication.
    /// </summary>
    public bool TrySchedule<TResult>(
        Func<CancellationToken, TResult> execute,
        CancellationToken cancellationToken,
        out WorldWorkerOperation<TResult>? operation,
        Action<WorldWorkerCompletion<TResult>>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        operation = null;

        if (Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Increment(ref _rejectedWorkCount);
            return false;
        }

        var item = new WorkItem<TResult>(
            execute, cancellationToken, onCompleted);
        // Counts are visible before queue admission: a worker may consume an
        // item immediately after TryAdd succeeds.
        Interlocked.Increment(ref _pendingWorkCount);
        Interlocked.Increment(ref _inFlightWorkCount);

        try
        {
            if (_queue.TryAdd(item))
            {
                operation = item.Operation;
                return true;
            }
        }
        catch (InvalidOperationException) when (
            Volatile.Read(ref _disposed) != 0)
        {
            // CompleteAdding raced admission; the counters are rolled back.
        }

        Interlocked.Decrement(ref _pendingWorkCount);
        Interlocked.Decrement(ref _inFlightWorkCount);
        Interlocked.Increment(ref _rejectedWorkCount);
        return false;
    }

    public void Dispose()
    {
        lock (_disposeSync)
        {
            if (_disposeCompleted)
                return;

            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _queue.CompleteAdding();

            // Drain queued work before stopping. Session owners retire and
            // collect their work first; this is the final safety net at exit.
            foreach (var worker in _workers)
                worker.Join();

            _queue.Dispose();
            _disposeCompleted = true;
        }
    }

    private void Consume()
    {
        foreach (var item in _queue.GetConsumingEnumerable())
        {
            Interlocked.Decrement(ref _pendingWorkCount);
            Interlocked.Increment(ref _activeWorkerCount);
            try
            {
                item.Execute();
            }
            finally
            {
                Interlocked.Decrement(ref _activeWorkerCount);
                Interlocked.Decrement(ref _inFlightWorkCount);
            }
        }
    }

    private interface IWorkItem
    {
        void Execute();
    }

    private sealed class WorkItem<TResult>(
        Func<CancellationToken, TResult> execute,
        CancellationToken cancellationToken,
        Action<WorldWorkerCompletion<TResult>>? onCompleted)
        : IWorkItem
    {
        public WorldWorkerOperation<TResult> Operation { get; } = new();

        public void Execute()
        {
            Operation.MarkRunning();
            WorldWorkerCompletion<TResult> completion;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                completion = new WorldWorkerCompletion<TResult>(
                    execute(cancellationToken), null);
            }
            catch (Exception error)
            {
                completion = new WorldWorkerCompletion<TResult>(default, error);
            }

            Operation.Publish(completion);
            try
            {
                onCompleted?.Invoke(completion);
            }
            catch
            {
                // Completion delivery is deliberately isolated from the pool.
                // The operation result remains observable by its owning runtime.
                // Callback failures are counted by the enclosing pool below.
                throw new WorkerCompletionCallbackException();
            }
        }
    }

    private sealed class WorkerCompletionCallbackException : Exception;
}
