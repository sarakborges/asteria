namespace Asteria.Core.World;

internal sealed class SingleFlightWorker<TResult>
    where TResult : class
{
    private Task<TResult>? _task;

    public bool IsRunning => _task is not null;

    public bool TryStart(Func<TResult> work)
    {
        ArgumentNullException.ThrowIfNull(work);

        if (_task is not null)
        {
            return false;
        }

        _task = Task.Run(work);
        return true;
    }

    public bool TryTakeCompleted(
        out TResult? result,
        out Exception? error)
    {
        if (_task is null ||
            !_task.IsCompleted)
        {
            result = null;
            error = null;
            return false;
        }

        var task = _task;
        _task = null;

        if (task.IsCanceled)
        {
            result = null;
            error =
                new TaskCanceledException(task);
            return true;
        }

        if (task.IsFaulted)
        {
            result = null;
            error =
                task.Exception?.GetBaseException() ??
                new InvalidOperationException(
                    "World worker task faulted without an exception.");
            return true;
        }

        result = task.Result;
        error = null;
        return true;
    }
}
