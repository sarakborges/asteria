using System.Collections.Concurrent;

namespace Asteria.Core.World;

/// <summary>
/// One immutable CPU operation forked by a generation step. Execute must work
/// only with detached inputs and return a detached result payload. The worker
/// pool never reads or mutates live VoxelWorld or dispatcher state.
/// </summary>
public readonly record struct ChunkGenerationWorkerRequest(
    ChunkGenerationTaskInstance Task,
    ulong WaitToken,
    ulong DependencyId,
    Func<CancellationToken, object?> Execute,
    CancellationToken CancellationToken = default)
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
        ArgumentNullException.ThrowIfNull(Execute);
    }
}

/// <summary>
/// Bounded shared CPU pool for independent world-generation work. A fixed
/// number of dedicated workers consume a bounded queue; each operation runs
/// one CPU slice and returns instead of waiting on dependencies. Workers block
/// only while idle waiting for another ready item. Completion capacity is
/// reserved before an operation is admitted.
/// </summary>
public sealed class ChunkGenerationWorkerPool : IAsyncDisposable
{
    private readonly BlockingCollection<WorkItem> _queue;
    private readonly ChunkGenerationCompletionMailbox _completionMailbox;
    private readonly Thread[] _workers;
    private readonly TaskCompletionSource _workersExited =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _disposed;
    private int _pendingWorkCount;
    private int _activeWorkerCount;
    private int _inFlightWorkCount;
    private int _exitedWorkerCount;
    private long _completedWorkCount;
    private long _failedWorkCount;
    private long _rejectedWorkCount;

    public ChunkGenerationWorkerPool(
        int workerCount,
        int pendingCapacity,
        ChunkGenerationCompletionMailbox completionMailbox)
    {
        if (workerCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(workerCount));
        if (pendingCapacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(pendingCapacity));

        _completionMailbox = completionMailbox ??
            throw new ArgumentNullException(nameof(completionMailbox));
        WorkerCount = workerCount;
        PendingCapacity = pendingCapacity;
        _queue = new BlockingCollection<WorkItem>(
            new ConcurrentQueue<WorkItem>(),
            pendingCapacity);

        _workers = new Thread[workerCount];
        for (var index = 0; index < workerCount; index++)
        {
            var worker = new Thread(Consume)
            {
                IsBackground = true,
                Name = $"Asteria worldgen worker {index + 1}",
            };
            _workers[index] = worker;
            worker.Start();
        }
    }

    public int WorkerCount { get; }

    public int PendingCapacity { get; }

    public int PendingWorkCount => Volatile.Read(ref _pendingWorkCount);

    public int ActiveWorkerCount => Volatile.Read(ref _activeWorkerCount);

    public int InFlightWorkCount => Volatile.Read(ref _inFlightWorkCount);

    public long CompletedWorkCount => Interlocked.Read(ref _completedWorkCount);

    public long FailedWorkCount => Interlocked.Read(ref _failedWorkCount);

    public long RejectedWorkCount => Interlocked.Read(ref _rejectedWorkCount);

    /// <summary>
    /// Queues a CPU operation without blocking. False means either the bounded
    /// work queue or completion mailbox has no capacity; caller retains the
    /// request and retries from the coordinator after capacity becomes available.
    /// </summary>
    public bool TrySchedule(ChunkGenerationWorkerRequest request)
    {
        request.Validate();
        if (Volatile.Read(ref _disposed) != 0)
        {
            Interlocked.Increment(ref _rejectedWorkCount);
            return false;
        }

        if (!_completionMailbox.TryReserve(out var reservation))
        {
            Interlocked.Increment(ref _rejectedWorkCount);
            return false;
        }

        var item = new WorkItem(request, reservation!);
        // Publish counts before TryAdd: a worker may consume immediately.
        Interlocked.Increment(ref _inFlightWorkCount);
        Interlocked.Increment(ref _pendingWorkCount);
        try
        {
            if (_queue.TryAdd(item))
                return true;
        }
        catch (InvalidOperationException) when (
            Volatile.Read(ref _disposed) != 0)
        {
            // CompleteAdding raced admission. The reservation is returned below.
        }

        Interlocked.Decrement(ref _pendingWorkCount);
        Interlocked.Decrement(ref _inFlightWorkCount);
        reservation!.Dispose();
        Interlocked.Increment(ref _rejectedWorkCount);
        return false;
    }

    /// <summary>
    /// Completes admission and asynchronously drains all queued/in-flight
    /// operations. No new operation is accepted after disposal starts.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
            _queue.CompleteAdding();

        return new ValueTask(_workersExited.Task);
    }

    private void Consume()
    {
        try
        {
            foreach (var item in _queue.GetConsumingEnumerable())
            {
                Interlocked.Decrement(ref _pendingWorkCount);
                Interlocked.Increment(ref _activeWorkerCount);
                Execute(item);
            }
        }
        finally
        {
            if (Interlocked.Increment(ref _exitedWorkerCount) == WorkerCount)
                _workersExited.TrySetResult();
        }
    }

    private void Execute(WorkItem item)
    {
        object? result = null;
        Exception? error = null;
        try
        {
            item.Request.CancellationToken.ThrowIfCancellationRequested();
            result = item.Request.Execute(item.Request.CancellationToken);
        }
        catch (Exception exception)
        {
            error = exception;
        }

        try
        {
            var completion = new ChunkGenerationDependencyCompletion(
                item.Request.Task,
                item.Request.WaitToken,
                item.Request.DependencyId,
                Result: result,
                Error: error);
            if (!item.Reservation.TryPublish(completion))
            {
                // A scheduled item owns its reservation exclusively. Reaching
                // this branch indicates an internal lifetime violation.
                Interlocked.Increment(ref _failedWorkCount);
                return;
            }

            if (error is null)
                Interlocked.Increment(ref _completedWorkCount);
            else
                Interlocked.Increment(ref _failedWorkCount);
        }
        finally
        {
            // No-op after successful publication; releases only if an
            // unexpected publication failure left the lease unused.
            item.Reservation.Dispose();
            Interlocked.Decrement(ref _activeWorkerCount);
            Interlocked.Decrement(ref _inFlightWorkCount);
        }
    }

    private sealed record WorkItem(
        ChunkGenerationWorkerRequest Request,
        ChunkGenerationCompletionReservation Reservation);
}
