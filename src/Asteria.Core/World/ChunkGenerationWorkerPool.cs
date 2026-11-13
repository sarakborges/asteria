using System.Threading.Channels;

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
/// number of asynchronous worker loops consume a bounded channel; each
/// operation runs one CPU slice and returns instead of waiting on dependencies.
/// Completion capacity is reserved before the operation is queued.
/// </summary>
public sealed class ChunkGenerationWorkerPool : IAsyncDisposable
{
    private readonly Channel<WorkItem> _channel;
    private readonly ChunkGenerationCompletionMailbox _completionMailbox;
    private readonly Task[] _workers;
    private int _disposed;
    private int _pendingWorkCount;
    private int _activeWorkerCount;
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
        _channel = Channel.CreateBounded<WorkItem>(
            new BoundedChannelOptions(pendingCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = workerCount == 1,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });

        _workers = new Task[workerCount];
        for (var index = 0; index < workerCount; index++)
            _workers[index] = Task.Run(ConsumeAsync);
    }

    public int WorkerCount { get; }

    public int PendingCapacity { get; }

    public int PendingWorkCount => Volatile.Read(ref _pendingWorkCount);

    public int ActiveWorkerCount => Volatile.Read(ref _activeWorkerCount);

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
        // Publish the count before TryWrite: a worker may consume immediately.
        Interlocked.Increment(ref _pendingWorkCount);
        if (_channel.Writer.TryWrite(item))
            return true;

        Interlocked.Decrement(ref _pendingWorkCount);
        reservation!.Dispose();
        Interlocked.Increment(ref _rejectedWorkCount);
        return false;
    }

    /// <summary>
    /// Completes admission and asynchronously drains all queued/in-flight
    /// operations. No new operation is accepted after disposal starts.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _channel.Writer.TryComplete();
        await Task.WhenAll(_workers).ConfigureAwait(false);
    }

    private async Task ConsumeAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync().ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _pendingWorkCount);
            Interlocked.Increment(ref _activeWorkerCount);

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
                    continue;
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
            }
        }
    }

    private sealed record WorkItem(
        ChunkGenerationWorkerRequest Request,
        ChunkGenerationCompletionReservation Reservation);
}
