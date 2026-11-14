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

    internal WorldWorkerOperation(long enqueueSequence)
    {
        EnqueueSequence = enqueueSequence;
    }

    internal long EnqueueSequence { get; }

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
/// Priority admission layer over the CLR's shared work-stealing ThreadPool.
/// It does not create or own worker threads: the runtime manages worker
/// activation and local queues, and idle workers can steal queued operations.
/// Asteria orders work before dispatch and caps the number of its own world
/// operations admitted to concurrent execution.
/// </summary>
public sealed class WorldWorkerPool : IDisposable, IAsyncDisposable
{
    public const int PlayerChunkPriority = 0;
    public const int ForwardPrefetchPriority = 10;
    public const int InteractiveWorldPriority = 20;
    public const int BackgroundChunkPriority = 30;
    public const int RearChunkPriority = 40;
    public const int BackgroundWorldPriority = 100;
    public const int DefaultPriority = BackgroundWorldPriority;

    private readonly object _queueSync = new();
    private readonly SortedSet<IWorkItem> _queue = new(WorkItemComparer.Instance);
    private readonly Dictionary<long, IWorkItem> _queuedItemsBySequence = [];
    private readonly TaskCompletionSource _drained =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _addingCompleted;
    private bool _disposeCompleted;
    private long _nextEnqueueSequence = 1;
    private int _disposed;
    private int _reservedOperationCount;
    private int _pendingWorkCount;
    private int _activeWorkerCount;
    private int _inFlightWorkCount;
    private long _completedWorkCount;
    private long _failedWorkCount;
    private long _cancelledWorkCount;
    private long _rejectedWorkCount;
    private long _completionCallbackFailures;

    public WorldWorkerPool(int maximumConcurrentOperations)
    {
        if (maximumConcurrentOperations <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maximumConcurrentOperations));

        WorkerCount = maximumConcurrentOperations;
    }

    /// <summary>
    /// Maximum number of Asteria world operations dispatched concurrently.
    /// This is an admission limit, not a count of threads owned by Asteria.
    /// </summary>
    public int WorkerCount { get; }

    /// <summary>
    /// Accepted operations that have not started executing yet, including
    /// operations already handed to the CLR ThreadPool.
    /// </summary>
    public int PendingWorkCount => Volatile.Read(ref _pendingWorkCount);

    /// <summary>World operations currently executing on CLR ThreadPool workers.</summary>
    public int ActiveWorkerCount => Volatile.Read(ref _activeWorkerCount);

    /// <summary>Accepted world operations that have not published completion.</summary>
    public int InFlightWorkCount => Volatile.Read(ref _inFlightWorkCount);

    public long CompletedWorkCount => Interlocked.Read(ref _completedWorkCount);

    public long FailedWorkCount => Interlocked.Read(ref _failedWorkCount);

    public long CancelledWorkCount => Interlocked.Read(ref _cancelledWorkCount);

    public long RejectedWorkCount => Interlocked.Read(ref _rejectedWorkCount);

    public long CompletionCallbackFailures =>
        Interlocked.Read(ref _completionCallbackFailures);

    /// <summary>
    /// Enqueues one operation without blocking. Lower numeric priority is
    /// dispatched first; equal priorities preserve submission order. The
    /// priority queue feeds the shared CLR pool only as concurrency slots open.
    /// </summary>
    public bool TrySchedule<TResult>(
        Func<CancellationToken, TResult> execute,
        CancellationToken cancellationToken,
        out WorldWorkerOperation<TResult>? operation,
        Action? onStarted = null,
        Action<WorldWorkerCompletion<TResult>>? onCompleted = null,
        int priority = DefaultPriority) =>
        TrySchedule(
            execute, cancellationToken, out operation,
            WorldWorkerPriority.FromPriorityClass(priority),
            onStarted, onCompleted);

    public bool TrySchedule<TResult>(
        Func<CancellationToken, TResult> execute,
        CancellationToken cancellationToken,
        out WorldWorkerOperation<TResult>? operation,
        WorldWorkerPriority priority,
        Action? onStarted = null,
        Action<WorldWorkerCompletion<TResult>>? onCompleted = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        operation = null;

        var item = new WorkItem<TResult>(
            execute, cancellationToken, onStarted, onCompleted);

        lock (_queueSync)
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                Interlocked.Increment(ref _rejectedWorkCount);
                return false;
            }

            if (_nextEnqueueSequence == long.MaxValue)
                throw new InvalidOperationException(
                    "World worker queue sequence was exhausted.");

            var enqueueSequence = _nextEnqueueSequence++;
            item.InitializeQueuePosition(enqueueSequence, priority);
            Interlocked.Increment(ref _pendingWorkCount);
            Interlocked.Increment(ref _inFlightWorkCount);
            if (!_queue.Add(item) ||
                !_queuedItemsBySequence.TryAdd(enqueueSequence, item))
            {
                Interlocked.Decrement(ref _pendingWorkCount);
                Interlocked.Decrement(ref _inFlightWorkCount);
                throw new InvalidOperationException(
                    "World worker queue rejected a unique operation.");
            }

            operation = item.Operation;
            DispatchQueuedOperationsLocked();
            return true;
        }
    }

    /// <summary>
    /// Changes the priority of an operation still waiting in Asteria's
    /// priority queue. Once dispatched to the CLR ThreadPool, its queue
    /// position is owned by the runtime and can no longer be changed here.
    /// </summary>
    public bool TryUpdatePriority<TResult>(
        WorldWorkerOperation<TResult> operation,
        int priority) =>
        TryUpdatePriority(
            operation, WorldWorkerPriority.FromPriorityClass(priority));

    public bool TryUpdatePriority<TResult>(
        WorldWorkerOperation<TResult> operation,
        WorldWorkerPriority priority)
    {
        ArgumentNullException.ThrowIfNull(operation);
        lock (_queueSync)
        {
            if (!_queuedItemsBySequence.TryGetValue(
                    operation.EnqueueSequence, out var item))
                return false;

            if (item.Priority == priority)
                return true;

            if (!_queue.Remove(item))
                throw new InvalidOperationException(
                    "A queued world worker operation was missing from its priority queue.");

            item.UpdatePriority(priority);
            if (!_queue.Add(item))
                throw new InvalidOperationException(
                    "A reprioritized world worker operation could not be reinserted.");
            return true;
        }
    }

    public ValueTask DisposeAsync()
    {
        lock (_queueSync)
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                _addingCompleted = true;

            DispatchQueuedOperationsLocked();
            return _disposeCompleted
                ? ValueTask.CompletedTask
                : new ValueTask(_drained.Task);
        }
    }

    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    private void DispatchQueuedOperationsLocked()
    {
        while (_reservedOperationCount < WorkerCount && _queue.Count > 0)
        {
            var item = _queue.Min ??
                throw new InvalidOperationException(
                    "World worker queue was nonempty without a minimum item.");
            if (!_queue.Remove(item) ||
                !_queuedItemsBySequence.Remove(item.EnqueueSequence))
            {
                throw new InvalidOperationException(
                    "World worker queue lost an admitted operation.");
            }

            _reservedOperationCount++;
            var dispatched = new DispatchedWork(this, item);
            if (ThreadPool.UnsafeQueueUserWorkItem(
                    static state => state.Owner.ExecuteDispatched(state.Item),
                    dispatched,
                    preferLocal: true))
            {
                continue;
            }

            _reservedOperationCount--;
            if (!_queue.Add(item) ||
                !_queuedItemsBySequence.TryAdd(item.EnqueueSequence, item))
            {
                throw new InvalidOperationException(
                    "A rejected ThreadPool operation could not be restored to the world queue.");
            }

            Interlocked.Increment(ref _rejectedWorkCount);
            throw new InvalidOperationException(
                "The CLR ThreadPool rejected a world worker operation.");
        }

        CompleteDrainIfNeededLocked();
    }

    private void ExecuteDispatched(IWorkItem item)
    {
        Interlocked.Decrement(ref _pendingWorkCount);
        Interlocked.Increment(ref _activeWorkerCount);
        try
        {
            item.Execute(this);
        }
        finally
        {
            Interlocked.Decrement(ref _activeWorkerCount);
            Interlocked.Decrement(ref _inFlightWorkCount);
            lock (_queueSync)
            {
                _reservedOperationCount--;
                DispatchQueuedOperationsLocked();
            }
        }
    }

    private void CompleteDrainIfNeededLocked()
    {
        if (_addingCompleted &&
            Volatile.Read(ref _inFlightWorkCount) == 0 &&
            _queue.Count == 0 &&
            _reservedOperationCount == 0)
        {
            _disposeCompleted = true;
            _drained.TrySetResult();
        }
    }

    private readonly record struct DispatchedWork(
        WorldWorkerPool Owner,
        IWorkItem Item);

    private interface IWorkItem
    {
        long EnqueueSequence { get; }
        WorldWorkerPriority Priority { get; }
        void InitializeQueuePosition(
            long enqueueSequence, WorldWorkerPriority priority);
        void UpdatePriority(WorldWorkerPriority priority);
        void Execute(WorldWorkerPool owner);
    }

    private sealed class WorkItemComparer : IComparer<IWorkItem>
    {
        public static WorkItemComparer Instance { get; } = new();

        public int Compare(IWorkItem? left, IWorkItem? right)
        {
            if (ReferenceEquals(left, right))
                return 0;
            if (left is null)
                return -1;
            if (right is null)
                return 1;

            var result = left.Priority.CompareTo(right.Priority);
            return result != 0
                ? result
                : left.EnqueueSequence.CompareTo(right.EnqueueSequence);
        }
    }

    private sealed class WorkItem<TResult>(
        Func<CancellationToken, TResult> execute,
        CancellationToken cancellationToken,
        Action? onStarted,
        Action<WorldWorkerCompletion<TResult>>? onCompleted)
        : IWorkItem
    {
        private WorldWorkerPriority _priority;
        private long _enqueueSequence;

        public WorldWorkerOperation<TResult> Operation { get; private set; } = null!;

        public long EnqueueSequence => _enqueueSequence;

        public WorldWorkerPriority Priority => _priority;

        public void InitializeQueuePosition(
            long enqueueSequence, WorldWorkerPriority priority)
        {
            _enqueueSequence = enqueueSequence;
            _priority = priority;
            Operation = new WorldWorkerOperation<TResult>(enqueueSequence);
        }

        public void UpdatePriority(WorldWorkerPriority priority) =>
            _priority = priority;

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
                // Publication failures must not escape onto the CLR worker pool.
                Interlocked.Increment(ref owner._completionCallbackFailures);
            }
        }
    }
}
