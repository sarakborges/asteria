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
/// mesh preparation. Runtime owners retain their logical priority queues and
/// submit bounded operations here; this executor applies a stable priority
/// order across domains and never touches live world state.
/// </summary>
public sealed class WorldWorkerPool : IDisposable, IAsyncDisposable
{
    public const int PlayerChunkPriority = 0;
    public const int InteractiveWorldPriority = 20;
    public const int BackgroundWorldPriority = 100;
    public const int DefaultPriority = BackgroundWorldPriority;

    private readonly object _queueSync = new();
    private readonly PriorityQueue<IWorkItem, QueuePriority> _queue = new();
    private readonly object _disposeSync = new();
    private readonly Thread[] _workers;
    private readonly TaskCompletionSource _workersExited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _addingCompleted;
    private bool _disposeCompleted;
    private long _nextEnqueueSequence = 1;
    private int _disposed;
    private int _exitedWorkerCount;
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

    /// <summary>
    /// Total additional operations the executor can accept, counting idle
    /// physical workers and the bounded queue behind them.
    /// </summary>
    public int AvailableSubmissionCount =>
        Math.Max(0, WorkerCount + PendingCapacity - InFlightWorkCount);

    public long CompletedWorkCount => Interlocked.Read(ref _completedWorkCount);

    public long FailedWorkCount => Interlocked.Read(ref _failedWorkCount);

    public long CancelledWorkCount => Interlocked.Read(ref _cancelledWorkCount);

    public long RejectedWorkCount => Interlocked.Read(ref _rejectedWorkCount);

    public long CompletionCallbackFailures =>
        Interlocked.Read(ref _completionCallbackFailures);

    /// <summary>
    /// Enqueues one operation without blocking. Lower numeric priority is
    /// dispatched first; equal priorities preserve submission order. A false
    /// result means the bounded waiting queue is full or the pool is retiring.
    /// </summary>
    public bool TrySchedule<TResult>(
        Func<CancellationToken, TResult> execute,
        CancellationToken cancellationToken,
        out WorldWorkerOperation<TResult>? operation,
        Action? onStarted = null,
        Action<WorldWorkerCompletion<TResult>>? onCompleted = null,
        int priority = DefaultPriority)
    {
        ArgumentNullException.ThrowIfNull(execute);
        operation = null;

        var item = new WorkItem<TResult>(
            execute, cancellationToken, onStarted, onCompleted);

        lock (_queueSync)
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                _pendingWorkCount >= PendingCapacity)
            {
                Interlocked.Increment(ref _rejectedWorkCount);
                return false;
            }

            if (_nextEnqueueSequence == long.MaxValue)
                throw new InvalidOperationException(
                    "World worker queue sequence was exhausted.");

            Interlocked.Increment(ref _pendingWorkCount);
            Interlocked.Increment(ref _inFlightWorkCount);
            _queue.Enqueue(
                item,
                new QueuePriority(priority, _nextEnqueueSequence++));
            operation = item.Operation;
            Monitor.Pulse(_queueSync);
            return true;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_disposeSync)
        {
            if (_disposeCompleted)
                return ValueTask.CompletedTask;

            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                lock (_queueSync)
                {
                    _addingCompleted = true;
                    Monitor.PulseAll(_queueSync);
                }
            }

            return new ValueTask(_workersExited.Task);
        }
    }

    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void Consume()
    {
        try
        {
            while (true)
            {
                IWorkItem item;
                lock (_queueSync)
                {
                    while (_queue.Count == 0 && !_addingCompleted)
                        Monitor.Wait(_queueSync);

                    if (_queue.Count == 0 && _addingCompleted)
                        break;

                    item = _queue.Dequeue();
                    Interlocked.Decrement(ref _pendingWorkCount);
                    Interlocked.Increment(ref _activeWorkerCount);
                }

                try
                {
                    item.Execute(this);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeWorkerCount);
                    Interlocked.Decrement(ref _inFlightWorkCount);
                }
            }
        }
        finally
        {
            if (Interlocked.Increment(ref _exitedWorkerCount) == WorkerCount)
            {
                lock (_disposeSync)
                    _disposeCompleted = true;
                _workersExited.TrySetResult();
            }
        }
    }

    private interface IWorkItem
    {
        void Execute(WorldWorkerPool owner);
    }

    private readonly record struct QueuePriority(
        int Priority,
        long EnqueueSequence) : IComparable<QueuePriority>
    {
        public int CompareTo(QueuePriority other)
        {
            var result = Priority.CompareTo(other.Priority);
            return result != 0
                ? result
                : EnqueueSequence.CompareTo(other.EnqueueSequence);
        }
    }

    private sealed class WorkItem<TResult>(
        Func<CancellationToken, TResult> execute,
        CancellationToken cancellationToken,
        Action? onStarted,
        Action<WorldWorkerCompletion<TResult>>? onCompleted)
        : IWorkItem
    {
        public WorldWorkerOperation<TResult> Operation { get; } = new();

        public void Execute(WorldWorkerPool owner)
        {
            Operation.MarkRunning();
            try
            {
                onStarted?.Invoke();
            }
            catch
            {
                Interlocked.Increment(ref owner._completionCallbackFailures);
            }

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
            if (completion.Succeeded)
                Interlocked.Increment(ref owner._completedWorkCount);
            else if (completion.Error is OperationCanceledException)
                Interlocked.Increment(ref owner._cancelledWorkCount);
            else
                Interlocked.Increment(ref owner._failedWorkCount);

            try
            {
                onCompleted?.Invoke(completion);
            }
            catch
            {
                // A publication callback must never terminate a physical worker.
                Interlocked.Increment(ref owner._completionCallbackFailures);
            }
        }
    }
}
