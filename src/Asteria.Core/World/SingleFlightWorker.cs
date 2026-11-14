namespace Asteria.Core.World;

/// <summary>
/// One logical in-flight operation backed by the session's shared world
/// executor. It never creates a thread or schedules work on Task.Run.
/// </summary>
internal sealed class SingleFlightWorker<TResult>
    where TResult : class
{
    private readonly WorldWorkerPool _workerPool;
    private WorldWorkerOperation<TResult>? _operation;

    public SingleFlightWorker(WorldWorkerPool workerPool)
    {
        _workerPool = workerPool ??
            throw new ArgumentNullException(nameof(workerPool));
    }

    /// <summary>
    /// True while this worker owns a queued, executing, or uncollected result.
    /// A completed operation remains owned until its coordinator polls it.
    /// </summary>
    public bool IsRunning => _operation is not null;

    public bool TryStart(
        Func<TResult> work,
        CancellationToken cancellationToken = default,
        int priority = WorldWorkerPool.DefaultPriority)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (_operation is not null)
            return false;

        return _workerPool.TrySchedule(
            _ => work(),
            cancellationToken,
            out _operation,
            priority: priority);
    }

    public bool TryTakeCompleted(
        out TResult? result,
        out Exception? error)
    {
        result = null;
        error = null;

        var operation = _operation;
        if (operation is null ||
            !operation.TryGetCompletion(out var completion))
            return false;

        _operation = null;
        result = completion.Result;
        error = completion.Error;
        return true;
    }
}
