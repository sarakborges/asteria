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
    /// Pending CPU operations are admitted only up to the currently available
    /// physical worker slots, then newly-created operations are admitted in a
    /// second pass. Chunk-task count itself is not an execution limit.
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
            var completionsProcessed = CollectCompletions(
                budget, out var staleCompletions,
                out var tasksFailed, out var tasksCancelled);
            var workerStarts = DispatchPendingWorkerOperations(
                maximumWorkerStarts);
            var tasksAdvanced = AdvanceReadyTasks(
                budget, out var tasksCompleted, ref tasksFailed);
            workerStarts += DispatchPendingWorkerOperations(
                maximumWorkerStarts - workerStarts);

            return new ChunkGenerationDispatchReport(
                completionsProcessed,
                staleCompletions,
                tasksAdvanced,
                tasksCompleted,
                tasksFailed,
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

        while (!budget.Exhausted(Stopwatch.GetTimestamp()) &&
               _completionMailbox.TryDequeue(out var completion))
        {
            processed++;
            if (!_allTasks.TryGetValue(completion.Task, out var entry) ||
                entry.InFlightOperations <= 0)
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

    private int DispatchPendingWorkerOperations(int maximumStarts)
    {
        var started = 0;
        while (started < maximumStarts &&
               _pendingWorkerTasks.TryPeek(out var queued))
        {
            if (!_currentTasks.TryGetValue(queued.Key, out var entry) ||
                entry.Lifecycle.State != ChunkGenerationTaskState.WaitingForDependencies ||
                entry.PendingWorkerOperations.Count == 0)
            {
                _pendingWorkerTasks.TryDequeue(out _);
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
                Priority: checked(entry.Priority.TicketLevel * 10));

            // Leave the admission queue untouched when either bounded queue is full.
            if (!_workerPool.TrySchedule(request))
                break;

            _pendingWorkerTasks.TryDequeue(out _);
            entry.PendingWorkerOperations.Dequeue();
            entry.InFlightOperations++;
            started++;

            // Rotate a task with more forked work behind peers at the same
            // priority so one large column cannot monopolize every worker slot.
            if (entry.PendingWorkerOperations.Count > 0)
            {
                _pendingWorkerTasks.Enqueue(
                    entry.Instance.Key,
                    entry.Priority,
                    entry.EnqueuedTimestamp);
            }
        }

        return started;
    }

    private int AdvanceReadyTasks(
        WorldFrameWorkBudget budget,
        out int tasksCompleted,
        ref int tasksFailed)
    {
        tasksCompleted = 0;
        var advanced = 0;

        while (!budget.Exhausted(Stopwatch.GetTimestamp()) &&
               _readyTasks.TryDequeue(out var queued))
        {
            if (!_currentTasks.TryGetValue(queued.Key, out var entry) ||
                !entry.Lifecycle.TryBeginExecution())
                continue;

            advanced++;
            var completedDependencies = entry.CompletedDependencies
                .OrderBy(item => item.DependencyId)
                .ToArray();
            entry.CompletedDependencies.Clear();

            try
            {
                entry.Cancellation.Token.ThrowIfCancellationRequested();
                var result = entry.Task.Advance(
                    completedDependencies,
                    entry.Cancellation.Token) ??
                    throw new InvalidOperationException(
                        $"Generation task returned a null advance result: {entry.Instance}");

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

                    default:
                        throw new ArgumentOutOfRangeException(nameof(result));
                }
            }
            catch (Exception error)
            {
                FailEntry(entry, error);
                tasksFailed++;
                if (entry.InFlightOperations == 0)
                    RetireEntry(entry);
            }
        }

        return advanced;
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
            entry.PendingWorkerOperations.Count != 0)
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

        public List<ChunkGenerationDependencyCompletion> CompletedDependencies { get; } = [];

        public Queue<ChunkGenerationWorkerOperation> PendingWorkerOperations { get; } = [];

        public int InFlightOperations { get; set; }

        public bool OutcomePublished { get; set; }
    }
}
