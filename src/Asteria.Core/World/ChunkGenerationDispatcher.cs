using System.Diagnostics;

namespace Asteria.Core.World;

/// <summary>Metrics for one serial generation-dispatcher pump.</summary>
public readonly record struct ChunkGenerationDispatchReport(
    int CompletionsProcessed,
    int StaleCompletionsIgnored,
    int TasksAdvanced,
    int TasksCompleted,
    int TasksFailed,
    int TasksCancelled,
    int WorkerOperationsStarted,
    int ReadyTasksRemaining,
    int WaitingTasks,
    int PendingWorkerOperations,
    int RetiringTasks);

/// <summary>
/// Serial per-session world-generation coordinator. It owns task lifecycle,
/// priorities and continuation transitions. Worker threads communicate only
/// through the completion mailbox and never mutate this object.
/// </summary>
public sealed class ChunkGenerationDispatcher
{
    private readonly int _coordinatorThreadId;
    private readonly ChunkGenerationTaskQueue _readyTasks = new();
    private readonly ChunkGenerationTaskQueue _pendingWorkerTasks = new();
    private readonly Dictionary<ChunkGenerationTaskKey, TaskEntry> _currentTasks = [];
    private readonly Dictionary<ChunkGenerationTaskInstance, TaskEntry> _allTasks = [];
    private readonly Queue<ChunkGenerationTaskOutcome> _outcomes = [];
    private readonly ChunkGenerationCompletionMailbox _completionMailbox;
    private readonly ChunkGenerationWorkerPool _workerPool;
    private readonly ChunkGenerationSerialExecutor _worldgenExecutor;
    private readonly ChunkGenerationSerialExecutor _lightingExecutor;
    private bool _preferLightingCompletion;
    private bool _isPumping;
    private long _completedTaskCount;
    private long _failedTaskCount;
    private long _cancelledTaskCount;

    public ChunkGenerationDispatcher(
        ChunkGenerationCompletionMailbox completionMailbox,
        ChunkGenerationWorkerPool workerPool)
    {
        _completionMailbox = completionMailbox ??
            throw new ArgumentNullException(nameof(completionMailbox));
        _workerPool = workerPool ??
            throw new ArgumentNullException(nameof(workerPool));
        _worldgenExecutor = new ChunkGenerationSerialExecutor(_workerPool);
        _lightingExecutor = new ChunkGenerationSerialExecutor(_workerPool);
        _coordinatorThreadId = Environment.CurrentManagedThreadId;
    }

    public int ActiveTaskCount => _currentTasks.Count;

    public int ReadyTaskCount => _readyTasks.Count;

    public int WaitingTaskCount =>
        _currentTasks.Values.Count(entry =>
            entry.Lifecycle.State == ChunkGenerationTaskState.WaitingForDependencies);

    public int RetiringTaskCount => _allTasks.Count - _currentTasks.Count;

    public int PendingWorkerOperationCount =>
        _currentTasks.Values.Sum(entry => entry.PendingWorkerOperations.Count);

    public int PendingOutcomeCount => _outcomes.Count;

    /// <summary>Whether this dimension currently owns its single serial task-advance slot.</summary>
    public bool HasTaskAdvanceInFlight => _worldgenExecutor.HasInFlightTask;

    public bool HasLightingOperationInFlight => _lightingExecutor.HasInFlightTask;

    /// <summary>Raised on the owning coordinator thread when a task advances its achieved holder status.</summary>
    public event Action<ChunkGenerationTaskInstance, ChunkGenerationStatus>? GenerationStatusAdvanced;

    /// <summary>Raised on the owning coordinator thread for one exact vertical section.</summary>
    public event Action<ChunkGenerationTaskInstance, ChunkSectionGenerationProgress>? SectionGenerationStatusAdvanced;

    internal ChunkGenerationSerialExecutor LightingExecutor => _lightingExecutor;

    /// <summary>
    /// Retrieves one terminal outcome for main-thread publication or cleanup.
    /// The caller must validate the task generation and current streaming ticket
    /// before inserting a completed Chunk into the live world.
    /// </summary>
    public bool TryDequeueOutcome(out ChunkGenerationTaskOutcome outcome)
    {
        EnsureCoordinatorThread();
        if (_outcomes.Count == 0)
        {
            outcome = default;
            return false;
        }

        outcome = _outcomes.Dequeue();
        return true;
    }

    /// <summary>
    /// Returns an already-collected terminal outcome when the caller's per-frame
    /// publication budget is exhausted. The coordinator remains the sole owner.
    /// </summary>
    public void RequeueOutcome(ChunkGenerationTaskOutcome outcome)
    {
        EnsureCoordinatorThread();
        _outcomes.Enqueue(outcome);
    }

    public long CompletedTaskCount => Interlocked.Read(ref _completedTaskCount);

    public long FailedTaskCount => Interlocked.Read(ref _failedTaskCount);

    public long CancelledTaskCount => Interlocked.Read(ref _cancelledTaskCount);

    /// <summary>
    /// Registers one logical column task. Only one active request per column
    /// and target step is allowed; a later retry must use a new generation ID.
    /// </summary>
    public bool TryEnqueue(
        ChunkGenerationTaskKey key,
        ulong generation,
        ChunkGenerationTaskPriority priority,
        long enqueuedTimestamp,
        IChunkGenerationTask task)
    {
        EnsureCoordinatorThread();
        ArgumentNullException.ThrowIfNull(task);
        var instance = new ChunkGenerationTaskInstance(key, generation);
        instance.Validate();

        if (_currentTasks.ContainsKey(key) || _allTasks.ContainsKey(instance))
            return false;

        var entry = new TaskEntry(instance, task, priority, enqueuedTimestamp);
        _allTasks.Add(instance, entry);
        _currentTasks.Add(key, entry);
        if (!_readyTasks.Enqueue(key, priority, enqueuedTimestamp))
        {
            _currentTasks.Remove(key);
            _allTasks.Remove(instance);
            entry.Cancellation.Dispose();
            throw new InvalidOperationException(
                $"Ready queue already contains a newly registered generation task: {key}");
        }

        return true;
    }

    /// <summary>
    /// Raises the target of an in-flight column task without replacing its
    /// identity. The task resumes from its achieved status when the new target
    /// is beyond the status at which it was parked.
    /// </summary>
    public bool TryUpgradeTargetStatus(
        ChunkGenerationTaskKey key,
        ChunkGenerationStatus targetStatus)
    {
        EnsureCoordinatorThread();
        if (!_currentTasks.TryGetValue(key, out var entry) ||
            entry.Task is not ChunkColumnMaterializationGenerationTask columnTask)
            return false;

        return columnTask.TryUpgradeTargetStatus(targetStatus);
    }

    /// <summary>
    /// Updates the current priority of a task in either ready or worker-admission
    /// queues. The task's original enqueue timestamp is preserved.
    /// </summary>
    public bool UpdatePriority(
        ChunkGenerationTaskKey key,
        ChunkGenerationTaskPriority priority)
    {
        EnsureCoordinatorThread();
        if (!_currentTasks.TryGetValue(key, out var entry))
            return false;

        entry.Priority = priority;
        _readyTasks.UpdatePriority(key, priority);
        _pendingWorkerTasks.UpdatePriority(key, priority);
        var workerPriority =
            WorldWorkerPriority.FromGenerationTicket(priority);
        foreach (var operation in entry.InFlightWorkerOperations.Values)
            _workerPool.TryUpdatePriority(operation, workerPriority);
        _worldgenExecutor.TryUpdatePriority(entry.Instance, workerPriority);
        _lightingExecutor.TryUpdatePriority(entry.Instance, workerPriority);
        return true;
    }

    /// <summary>
    /// Cancels a task, removes unstarted operations and signals its running
    /// workers. The entry remains tracked until all already-started operations
    /// publish their completion messages, so old results cannot affect a retry.
    /// </summary>
    public bool TryCancel(ChunkGenerationTaskKey key)
    {
        EnsureCoordinatorThread();
        if (!_currentTasks.TryGetValue(key, out var entry) ||
            !entry.Lifecycle.TryCancel())
            return false;

        RemoveFromActiveQueues(entry);
        entry.PendingWorkerOperations.Clear();
        entry.Cancellation.Cancel();
        CancelFutureWaits(entry);
        Interlocked.Increment(ref _cancelledTaskCount);
        if (entry.InFlightOperations == 0)
            RetireEntry(entry);
        return true;
    }

    /// <summary>
    /// Cancels every current task. Call Pump afterwards until RetiringTaskCount
    /// reaches zero to collect any outstanding worker completions.
    /// </summary>
    public int CancelAll()
    {
        EnsureCoordinatorThread();
        var keys = _currentTasks.Keys.ToArray();
        var cancelled = 0;
        foreach (var key in keys)
        {
            if (TryCancel(key))
                cancelled++;
        }

        return cancelled;
    }

    public bool TryGetTaskState(
        ChunkGenerationTaskKey key,
        out ChunkGenerationTaskState state)
    {
        EnsureCoordinatorThread();
        if (_currentTasks.TryGetValue(key, out var entry))
        {
            state = entry.Lifecycle.State;
            return true;
        }

        state = default;
        return false;
    }

    /// <summary>
    /// Runs serial continuation work within the caller's frame deadline.
    /// Ready chunk tasks submit detached CPU operations to the shared priority
    /// queue. The pool bounds physical execution; queued demand is not capped
    /// by a multiplier of the worker count.
    /// </summary>
    public ChunkGenerationDispatchReport Pump(
        WorldFrameWorkBudget budget,
        int maximumWorkerStarts)
    {
        EnsureCoordinatorThread();
        if (maximumWorkerStarts < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumWorkerStarts));
        if (_isPumping)
            throw new InvalidOperationException(
                "The generation dispatcher cannot be pumped reentrantly.");

        _isPumping = true;
        try
        {
            var advanceCompletionsProcessed = CollectTaskAdvanceCompletion(
                budget,
                out var advanceCompletionsStale,
                out var tasksCompleted,
                out var advanceTasksFailed);
            var completionsProcessed = CollectCompletions(
                budget, out var staleCompletions,
                out var dependencyTasksFailed, out var tasksCancelled);

            // One logical task advance per dimension is admitted to the shared
            // pool. Its callback is short and serial; dependency forks from
            // earlier advances remain independently parallel.
            var tasksAdvanced = ScheduleReadyTaskAdvance(
                budget, maximumWorkerStarts);
            var workerStarts = tasksAdvanced;
            workerStarts += DispatchPendingWorkerOperations(
                budget, maximumWorkerStarts - workerStarts);

            return new ChunkGenerationDispatchReport(
                completionsProcessed + advanceCompletionsProcessed,
                staleCompletions + advanceCompletionsStale,
                tasksAdvanced,
                tasksCompleted,
                advanceTasksFailed + dependencyTasksFailed,
                tasksCancelled,
                workerStarts,
                _readyTasks.Count,
                WaitingTaskCount,
                PendingWorkerOperationCount,
                RetiringTaskCount);
        }
        finally
        {
            _isPumping = false;
        }
    }

    private int CollectTaskAdvanceCompletion(
        WorldFrameWorkBudget budget,
        out int staleCompletions,
        out int tasksCompleted,
        out int tasksFailed)
    {
        staleCompletions = 0;
        tasksCompleted = 0;
        tasksFailed = 0;

        if (budget.Exhausted(Stopwatch.GetTimestamp()) ||
            !_worldgenExecutor.TryCollect(out var completion))
            return 0;

        if (!_allTasks.TryGetValue(completion.Task, out var entry) ||
            entry.InFlightOperations <= 0)
        {
            throw new InvalidOperationException(
                $"Task-advance completion did not match the active generation: {completion.Task}");
        }

        entry.InFlightOperations--;

        if (IsTerminal(entry.Lifecycle.State))
        {
            staleCompletions++;
            if (entry.InFlightOperations == 0)
                RetireEntry(entry);
            return 1;
        }

        if (completion.Error is not null)
        {
            FailEntry(entry, completion.Error);
            tasksFailed++;
            if (entry.InFlightOperations == 0)
                RetireEntry(entry);
            return 1;
        }

        var result = completion.Result ??
            throw new InvalidOperationException(
                $"Task advance completed without a result: {entry.Instance}");
        foreach (var sectionProgress in result.SectionProgress)
        {
            sectionProgress.Validate();
            if (ChunkColumnCoord.FromChunk(sectionProgress.Coord) !=
                entry.Instance.Key.Column)
            {
                throw new InvalidOperationException(
                    $"Generation task {entry.Instance} published progress for an unrelated section: {sectionProgress.Coord}");
            }

            SectionGenerationStatusAdvanced?.Invoke(
                entry.Instance,
                sectionProgress);
        }

        if (result.ProgressStatus is { } progressStatus &&
            progressStatus > entry.HighestReportedStatus)
        {
            entry.HighestReportedStatus = progressStatus;
            GenerationStatusAdvanced?.Invoke(entry.Instance, progressStatus);
        }
        switch (result.Kind)
        {
            case ChunkGenerationTaskAdvanceKind.Completed:
                CompleteEntry(entry, result.CompletionPayload);
                tasksCompleted++;
                break;

            case ChunkGenerationTaskAdvanceKind.Yield:
                if (!entry.Lifecycle.TryYieldForMoreWork())
                    throw new InvalidOperationException(
                        $"Generation task could not yield: {entry.Instance}");
                EnqueueReadyAgain(entry);
                break;

            case ChunkGenerationTaskAdvanceKind.WaitForWorkers:
                BeginWorkerWait(entry, result.WorkerOperations);
                break;

            case ChunkGenerationTaskAdvanceKind.WaitForFutures:
                BeginFutureWait(entry, result.FutureOperations);
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(result));
        }

        return 1;
    }

    private int CollectCompletions(
        WorldFrameWorkBudget budget,
        out int staleCompletions,
        out int tasksFailed,
        out int tasksCancelled)
    {
        staleCompletions = 0;
        tasksFailed = 0;
        tasksCancelled = 0;
        var processed = 0;

        while (!budget.Exhausted(Stopwatch.GetTimestamp()))
        {
            ChunkGenerationDependencyCompletion completion;
            var collected = _preferLightingCompletion
                ? _lightingExecutor.TryCollectDependency(out completion)
                : _completionMailbox.TryDequeue(out completion);
            if (!collected)
            {
                collected = _preferLightingCompletion
                    ? _completionMailbox.TryDequeue(out completion)
                    : _lightingExecutor.TryCollectDependency(out completion);
            }
            if (!collected)
                break;

            // Alternate the first mailbox examined. A steady stream of
            // materialization forks must not starve the serial light lane.
            _preferLightingCompletion = !_preferLightingCompletion;
            processed++;
            if (!_allTasks.TryGetValue(completion.Task, out var entry) ||
                entry.InFlightOperations <= 0)
            {
                staleCompletions++;
                continue;
            }

            var wasWorkerOperation =
                entry.InFlightWorkerDependencyIds.Remove(completion.DependencyId);
            entry.InFlightWorkerOperations.Remove(completion.DependencyId);
            var wasFutureOperation =
                entry.InFlightFutureOperations.Remove(completion.DependencyId);
            if (!wasWorkerOperation && !wasFutureOperation)
            {
                staleCompletions++;
                continue;
            }

            entry.InFlightOperations--;
            if (IsTerminal(entry.Lifecycle.State))
            {
                staleCompletions++;
                if (entry.InFlightOperations == 0)
                    RetireEntry(entry);
                continue;
            }

            if (completion.Error is not null)
            {
                FailEntry(entry, completion.Error);
                tasksFailed++;
                if (entry.InFlightOperations == 0)
                    RetireEntry(entry);
                continue;
            }

            var signal = entry.Lifecycle.SignalDependencyCompleted(
                completion.WaitToken, completion.DependencyId);
            switch (signal)
            {
                case ChunkGenerationDependencySignalResult.StillWaiting:
                    entry.CompletedDependencies.Add(completion);
                    break;

                case ChunkGenerationDependencySignalResult.ReadyToResume:
                    entry.CompletedDependencies.Add(completion);
                    if (!_readyTasks.Enqueue(
                            entry.Instance.Key,
                            entry.Priority,
                            entry.EnqueuedTimestamp))
                    {
                        throw new InvalidOperationException(
                            $"Could not enqueue resumed generation task: {entry.Instance}");
                    }
                    break;

                case ChunkGenerationDependencySignalResult.StaleWait:
                case ChunkGenerationDependencySignalResult.DuplicateOrUnknownDependency:
                    staleCompletions++;
                    break;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        return processed;
    }

    private int DispatchPendingWorkerOperations(
        WorldFrameWorkBudget budget,
        int maximumStarts)
    {
        var started = 0;
        var remainingScans = _pendingWorkerTasks.Count;
        while (started < maximumStarts &&
               remainingScans > 0 &&
               !budget.Exhausted(Stopwatch.GetTimestamp()) &&
               _pendingWorkerTasks.TryPeek(out var queued))
        {
            if (!_currentTasks.TryGetValue(queued.Key, out var entry) ||
                entry.Lifecycle.State != ChunkGenerationTaskState.WaitingForDependencies ||
                entry.PendingWorkerOperations.Count == 0)
            {
                _pendingWorkerTasks.TryDequeue(out _);
                remainingScans = Math.Max(0, remainingScans - 1);
                continue;
            }

            var operation = entry.PendingWorkerOperations.Peek();
            var waitToken = entry.Lifecycle.CurrentWaitToken ??
                throw new InvalidOperationException(
                    $"Waiting task has no active wait token: {entry.Instance}");
            var request = new ChunkGenerationWorkerRequest(
                entry.Instance,
                waitToken,
                operation.DependencyId,
                operation.Execute,
                entry.Cancellation.Token,
                Priority: WorldWorkerPriority.FromGenerationTicket(
                    entry.Priority));

            WorldWorkerOperation<object?>? workerOperation = null;
            var accepted = operation.ExecutionPolicy switch
            {
                ChunkGenerationExecutionPolicy.SharedWorkerPool =>
                    _workerPool.TrySchedule(request, out workerOperation),
                ChunkGenerationExecutionPolicy.LightExecutor =>
                    _lightingExecutor.TryScheduleDependency(request),
                _ => throw new InvalidOperationException(
                    $"Worker dependency {operation.DependencyId} has unsupported execution policy {operation.ExecutionPolicy}."),
            };

            if (!accepted)
            {
                // A busy light lane must not prevent unrelated worldgen forks
                // from using the shared pool. Rotate this task and inspect the
                // other queued task heads once before returning.
                if (operation.ExecutionPolicy == ChunkGenerationExecutionPolicy.LightExecutor)
                {
                    _pendingWorkerTasks.TryDequeue(out _);
                    if (!_pendingWorkerTasks.Enqueue(
                            queued.Key,
                            entry.Priority,
                            entry.EnqueuedTimestamp))
                        throw new InvalidOperationException(
                            $"Could not rotate a task waiting for the light executor: {entry.Instance}");
                    remainingScans--;
                    continue;
                }

                break;
            }

            _pendingWorkerTasks.TryDequeue(out _);
            entry.PendingWorkerOperations.Dequeue();
            entry.InFlightOperations++;
            if (!entry.InFlightWorkerDependencyIds.Add(operation.DependencyId))
                throw new InvalidOperationException(
                    $"Worker dependency {operation.DependencyId} was already in flight: {entry.Instance}");
            if (workerOperation is not null)
            {
                entry.InFlightWorkerOperations.Add(
                    operation.DependencyId,
                    workerOperation);
            }
            started++;

            // Rotate tasks with additional forked work behind peers at equal
            // priority so one column cannot monopolize every available lane.
            if (entry.PendingWorkerOperations.Count > 0)
            {
                _pendingWorkerTasks.Enqueue(
                    entry.Instance.Key,
                    entry.Priority,
                    entry.EnqueuedTimestamp);
            }

            remainingScans = _pendingWorkerTasks.Count;
        }

        return started;
    }

    private int ScheduleReadyTaskAdvance(
        WorldFrameWorkBudget budget,
        int maximumStarts)
    {
        if (maximumStarts <= 0 ||
            _worldgenExecutor.HasInFlightTask ||
            budget.Exhausted(Stopwatch.GetTimestamp()))
            return 0;

        while (_readyTasks.TryDequeue(out var queued))
        {
            if (!_currentTasks.TryGetValue(queued.Key, out var entry) ||
                !entry.Lifecycle.TryBeginExecution())
                continue;

            var completedDependencies = entry.CompletedDependencies
                .OrderBy(item => item.DependencyId)
                .ToArray();
            entry.CompletedDependencies.Clear();

            if (!_worldgenExecutor.TrySchedule(
                    entry.Instance,
                    token =>
                    {
                        token.ThrowIfCancellationRequested();
                        return entry.Task.Advance(completedDependencies, token) ??
                            throw new InvalidOperationException(
                                $"Generation task returned a null advance result: {entry.Instance}");
                    },
                    entry.Cancellation.Token,
                    WorldWorkerPriority.FromGenerationTicket(entry.Priority)))
            {
                var error = new InvalidOperationException(
                    $"The shared worker pool rejected a serial task advance: {entry.Instance}");
                FailEntry(entry, error);
                if (entry.InFlightOperations == 0)
                    RetireEntry(entry);
                return 0;
            }

            entry.InFlightOperations++;
            return 1;
        }

        return 0;
    }

    private void BeginWorkerWait(
        TaskEntry entry,
        IReadOnlyList<ChunkGenerationWorkerOperation> operations)
    {
        if (operations.Count == 0)
            throw new InvalidOperationException(
                $"A waiting task did not provide worker operations: {entry.Instance}");

        var waitToken = entry.Lifecycle.BeginWaitingForDependencies(
            operations.Select(operation => operation.DependencyId));
        if (waitToken == 0)
            throw new InvalidOperationException(
                $"Generation task received an invalid wait token: {entry.Instance}");

        foreach (var operation in operations)
            entry.PendingWorkerOperations.Enqueue(operation);

        if (!_pendingWorkerTasks.Enqueue(
                entry.Instance.Key,
                entry.Priority,
                entry.EnqueuedTimestamp))
            throw new InvalidOperationException(
                $"Worker admission queue already contains waiting task: {entry.Instance}");
    }

    private void BeginFutureWait(
        TaskEntry entry,
        IReadOnlyList<ChunkGenerationFutureOperation> operations)
    {
        if (operations.Count == 0)
            throw new InvalidOperationException(
                $"A waiting task did not provide holder futures: {entry.Instance}");

        var waitToken = entry.Lifecycle.BeginWaitingForDependencies(
            operations.Select(operation => operation.DependencyId));
        if (waitToken == 0)
            throw new InvalidOperationException(
                $"Generation task received an invalid future wait token: {entry.Instance}");

        // Admission belongs to the serial coordinator, not the task's
        // worker advance. This lets the request enqueue a producer on the same
        // dispatcher before the dependent task parks on its status future.
        // A future may complete between the IsCompleted check and admission,
        // so RequestProducer must be idempotent.
        try
        {
            foreach (var operation in operations)
            {
                if (!operation.Future.IsCompleted)
                    operation.RequestProducer?.Invoke();
            }
        }
        catch (Exception error)
        {
            FailEntry(entry, error);
            if (entry.InFlightOperations == 0)
                RetireEntry(entry);
            return;
        }

        foreach (var operation in operations)
        {
            if (!entry.InFlightFutureOperations.Add(operation.DependencyId))
                throw new InvalidOperationException(
                    $"Future dependency {operation.DependencyId} was registered twice: {entry.Instance}");
            entry.InFlightOperations++;

            // This continuation only publishes a detached completion message.
            // It never resumes a task or mutates dispatcher/lifecycle state, and
            // it does not occupy a physical worker while the holder future waits.
            _ = operation.Future.ContinueWith(
                completed =>
                {
                    if (completed.IsCanceled)
                    {
                        _completionMailbox.Publish(
                            new ChunkGenerationDependencyCompletion(
                                entry.Instance,
                                waitToken,
                                operation.DependencyId,
                                Error: new TaskCanceledException(completed)));
                    }
                    else if (completed.IsFaulted)
                    {
                        _completionMailbox.Publish(
                            new ChunkGenerationDependencyCompletion(
                                entry.Instance,
                                waitToken,
                                operation.DependencyId,
                                Error: completed.Exception?.InnerException ??
                                    (Exception?)completed.Exception ??
                                    new InvalidOperationException(
                                        "Holder status future faulted without an exception.")));
                    }
                    else
                    {
                        _completionMailbox.Publish(
                            new ChunkGenerationDependencyCompletion(
                                entry.Instance,
                                waitToken,
                                operation.DependencyId,
                                operation.GetResult()));
                    }
                },
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private void CancelFutureWaits(TaskEntry entry)
    {
        var cancelled = entry.InFlightFutureOperations.Count;
        if (cancelled == 0)
            return;

        entry.InFlightFutureOperations.Clear();
        entry.InFlightOperations -= cancelled;
        if (entry.InFlightOperations < 0)
            throw new InvalidOperationException(
                $"Future wait cancellation underflowed in-flight operations: {entry.Instance}");
    }

    private void EnqueueReadyAgain(TaskEntry entry)
    {
        if (!_readyTasks.Enqueue(
                entry.Instance.Key,
                entry.Priority,
                entry.EnqueuedTimestamp))
            throw new InvalidOperationException(
                $"Ready queue already contains yielded task: {entry.Instance}");
    }

    private void CompleteEntry(TaskEntry entry, object? completionPayload)
    {
        if (entry.InFlightOperations != 0 ||
            entry.PendingWorkerOperations.Count != 0 ||
            entry.InFlightFutureOperations.Count != 0)
        {
            throw new InvalidOperationException(
                $"Generation task completed while worker operations remained: {entry.Instance}");
        }

        if (!entry.Lifecycle.TryComplete())
            throw new InvalidOperationException(
                $"Generation task could not enter completed state: {entry.Instance}");

        RemoveFromActiveQueues(entry);
        _outcomes.Enqueue(new ChunkGenerationTaskOutcome(
            entry.Instance,
            ChunkGenerationTaskOutcomeKind.Completed,
            completionPayload,
            null));
        entry.OutcomePublished = true;
        Interlocked.Increment(ref _completedTaskCount);
        RetireEntry(entry);
    }

    private void FailEntry(TaskEntry entry, Exception error)
    {
        if (!entry.Lifecycle.TryFail(error))
            return;

        RemoveFromActiveQueues(entry);
        entry.PendingWorkerOperations.Clear();
        entry.Cancellation.Cancel();
        CancelFutureWaits(entry);
        _outcomes.Enqueue(new ChunkGenerationTaskOutcome(
            entry.Instance,
            ChunkGenerationTaskOutcomeKind.Failed,
            null,
            error));
        entry.OutcomePublished = true;
        Interlocked.Increment(ref _failedTaskCount);
    }

    private void RemoveFromActiveQueues(TaskEntry entry)
    {
        _readyTasks.Remove(entry.Instance.Key);
        _pendingWorkerTasks.Remove(entry.Instance.Key);
        if (_currentTasks.TryGetValue(entry.Instance.Key, out var current) &&
            ReferenceEquals(current, entry))
        {
            _currentTasks.Remove(entry.Instance.Key);
        }
    }

    private void RetireEntry(TaskEntry entry)
    {
        if (entry.InFlightOperations != 0)
            throw new InvalidOperationException(
                $"Cannot release a task with in-flight workers: {entry.Instance}");

        if (!entry.OutcomePublished &&
            entry.Lifecycle.State == ChunkGenerationTaskState.Cancelled)
        {
            _outcomes.Enqueue(new ChunkGenerationTaskOutcome(
                entry.Instance,
                ChunkGenerationTaskOutcomeKind.Cancelled,
                null,
                null));
            entry.OutcomePublished = true;
        }

        _allTasks.Remove(entry.Instance);
        if (entry.Task is IDisposable disposableTask)
            disposableTask.Dispose();
        entry.Cancellation.Dispose();
    }

    private static bool IsTerminal(ChunkGenerationTaskState state) =>
        state is ChunkGenerationTaskState.Completed or
            ChunkGenerationTaskState.Cancelled or
            ChunkGenerationTaskState.Failed;

    private void EnsureCoordinatorThread()
    {
        if (Environment.CurrentManagedThreadId != _coordinatorThreadId)
            throw new InvalidOperationException(
                "Generation dispatcher state may only be accessed by its owning serial coordinator thread.");
    }

    private sealed class TaskEntry(
        ChunkGenerationTaskInstance instance,
        IChunkGenerationTask task,
        ChunkGenerationTaskPriority priority,
        long enqueuedTimestamp)
    {
        public ChunkGenerationTaskInstance Instance { get; } = instance;

        public IChunkGenerationTask Task { get; } = task;

        public ChunkGenerationTaskPriority Priority { get; set; } = priority;

        public long EnqueuedTimestamp { get; } = enqueuedTimestamp;

        public ChunkGenerationTaskLifecycle Lifecycle { get; } = new();

        public CancellationTokenSource Cancellation { get; } = new();

        public ChunkGenerationStatus HighestReportedStatus { get; set; } =
            ChunkGenerationStatus.Unloaded;

        public List<ChunkGenerationDependencyCompletion> CompletedDependencies { get; } = [];

        public Queue<ChunkGenerationWorkerOperation> PendingWorkerOperations { get; } = [];

        public Dictionary<ulong, WorldWorkerOperation<object?>> InFlightWorkerOperations { get; } = [];

        public HashSet<ulong> InFlightWorkerDependencyIds { get; } = [];

        public HashSet<ulong> InFlightFutureOperations { get; } = [];

        public int InFlightOperations { get; set; }

        public bool OutcomePublished { get; set; }
    }
}
