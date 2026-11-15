using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationDispatcherTests
{
    [Fact]
    public async Task YieldedTaskReturnsBehindPeersAtEqualPriority()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var order = new List<string>();
        var first = new YieldOnceTask("first", order);
        var second = new CompleteImmediatelyTask("second", order);

        Assert.True(dispatcher.TryEnqueue(Key(1), 1, Priority(0), 1, first));
        Assert.True(dispatcher.TryEnqueue(Key(2), 1, Priority(0), 2, second));

        var report = PumpUntilIdle(dispatcher);

        Assert.Equal(3, report.TasksAdvanced);
        Assert.Equal(2, report.TasksCompleted);
        Assert.Equal(new[] { "first", "second", "first" }, order);
        Assert.Equal(0, dispatcher.ActiveTaskCount);
        Assert.Equal(2, dispatcher.CompletedTaskCount);

        var secondPump = dispatcher.Pump(GenerousBudget(), 8);

        Assert.Equal(0, secondPump.TasksAdvanced);
        Assert.Equal(2, dispatcher.CompletedTaskCount);
    }

    [Fact]
    public async Task RequeuedTerminalOutcomeRemainsAvailableExactlyOnce()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var payload = new CompletedChunkPayload("deferred", 16);
        Assert.True(dispatcher.TryEnqueue(
            Key(11), 4, Priority(0), 11, new ResultTask(payload)));

        PumpUntilIdle(dispatcher);

        Assert.True(dispatcher.TryDequeueOutcome(out var first));
        dispatcher.RequeueOutcome(first);
        Assert.True(dispatcher.TryDequeueOutcome(out var second));
        Assert.Equal(first, second);
        Assert.Same(payload, second.Result);
        Assert.False(dispatcher.TryDequeueOutcome(out _));
    }

    [Fact]
    public async Task CompletedTaskPublishesDetachedResultForWorldLoop()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var payload = new CompletedChunkPayload("chunk-10", 4096);

        Assert.True(dispatcher.TryEnqueue(
            Key(10), 3, Priority(0), 10, new ResultTask(payload)));
        PumpUntilIdle(dispatcher);

        Assert.Equal(1, dispatcher.PendingOutcomeCount);
        Assert.True(dispatcher.TryDequeueOutcome(out var outcome));
        Assert.Equal(ChunkGenerationTaskOutcomeKind.Completed, outcome.Kind);
        Assert.Equal(Key(10), outcome.Task.Key);
        Assert.Equal(3UL, outcome.Task.Generation);
        Assert.Same(payload, outcome.Result);
        Assert.Null(outcome.Error);
        Assert.False(dispatcher.TryDequeueOutcome(out _));
    }

    [Fact]
    public async Task WorkerDependenciesSuspendAndResumeTaskWithResults()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(2, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var task = new TwoWorkerTask();
        Assert.True(dispatcher.TryEnqueue(Key(3), 8, Priority(0), 10, task));

        var initial = dispatcher.Pump(GenerousBudget(), 4);

        Assert.Equal(1, initial.TasksAdvanced);
        Assert.Equal(1, initial.WorkerOperationsStarted);
        Assert.True(PumpUntilCondition(
            dispatcher,
            () => dispatcher.WaitingTaskCount == 1,
            maximumWorkerStarts: 4));
        Assert.True(dispatcher.TryGetTaskState(Key(3), out var waitingState));
        Assert.Equal(ChunkGenerationTaskState.WaitingForDependencies, waitingState);

        var final = PumpUntilIdle(dispatcher, maximumWorkerStarts: 4);

        Assert.Equal(2, task.ObservedResults.Count);
        Assert.Equal("terrain-a", task.ObservedResults[0]);
        Assert.Equal("terrain-b", task.ObservedResults[1]);
        Assert.Equal(1, dispatcher.CompletedTaskCount);
        Assert.Equal(0, dispatcher.WaitingTaskCount);
        Assert.Equal(0, dispatcher.RetiringTaskCount);
    }

    [Fact]
    public async Task PriorityCanChangeAfterEnqueueBeforeTaskStarts()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var order = new List<string>();

        Assert.True(dispatcher.TryEnqueue(
            Key(4), 1, Priority(3), 1, new CompleteImmediatelyTask("far", order)));
        Assert.True(dispatcher.TryEnqueue(
            Key(5), 1, Priority(2), 2, new CompleteImmediatelyTask("near", order)));
        Assert.True(dispatcher.UpdatePriority(Key(4), Priority(0)));

        var report = PumpUntilIdle(dispatcher);

        Assert.Equal(new[] { "far", "near" }, order);
        Assert.Equal(2, report.TasksCompleted);
        Assert.Equal(0, dispatcher.ActiveTaskCount);
    }

    [Fact]
    public async Task TicketChangesReprioritizeReadyTasksWithoutPreemptingAdmittedSerialAdvance()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        using var blockerStarted = new ManualResetEventSlim(false);
        using var releaseBlocker = new ManualResetEventSlim(false);
        var executionOrder = new System.Collections.Concurrent.ConcurrentQueue<string>();
        Assert.True(pool.WorkerPool.TrySchedule(
            _ =>
            {
                blockerStarted.Set();
                releaseBlocker.Wait();
                return "blocker";
            },
            CancellationToken.None,
            out _,
            priority: WorldWorkerPool.BackgroundWorldPriority));
        Assert.True(blockerStarted.Wait(TimeSpan.FromSeconds(5)));

        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        Assert.True(dispatcher.TryEnqueue(
            Key(14), 1, Priority(3), 14,
            new OrderedWorkerTask("far-when-enqueued", executionOrder)));
        Assert.True(dispatcher.TryEnqueue(
            Key(15), 1, Priority(2), 15,
            new OrderedWorkerTask("near-when-enqueued", executionOrder)));

        var admitted = dispatcher.Pump(GenerousBudget(), 2);
        Assert.Equal(1, admitted.WorkerOperationsStarted);
        Assert.Equal(1, pool.PendingWorkCount);
        Assert.True(dispatcher.UpdatePriority(Key(14), Priority(0)));

        releaseBlocker.Set();
        PumpUntilIdle(dispatcher, maximumWorkerStarts: 4);

        // The near task's serial advance was already admitted to the
        // per-dimension continuation lane before the far ticket changed.
        // Reprioritization applies to the next dispatch; it does not preempt
        // an advance already selected by the serial lane.
        Assert.Equal(
            new[] { "near-when-enqueued", "far-when-enqueued" },
            executionOrder.ToArray());
        Assert.Equal(2, dispatcher.CompletedTaskCount);
    }

    [Fact]
    public async Task WorkerFailureFailsTaskWithoutResumingIt()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var error = new InvalidOperationException("worker slice failed");
        var task = new FailingWorkerTask(error);
        Assert.True(dispatcher.TryEnqueue(Key(6), 1, Priority(0), 1, task));

        PumpUntilIdle(dispatcher, maximumWorkerStarts: 2);

        Assert.Equal(1, dispatcher.FailedTaskCount);
        Assert.Equal(0, task.ResumeCount);
        Assert.Equal(0, dispatcher.RetiringTaskCount);
        Assert.True(dispatcher.TryDequeueOutcome(out var outcome));
        Assert.Equal(ChunkGenerationTaskOutcomeKind.Failed, outcome.Kind);
        Assert.Same(error, outcome.Error);
        Assert.Null(outcome.Result);
    }

    [Fact]
    public async Task CancellationRejectsLateWorkerCompletionAndAllowsNewGeneration()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        using var workerStarted = new ManualResetEventSlim(false);

        var task = new BlockingWorkerTask(workerStarted);
        Assert.True(dispatcher.TryEnqueue(Key(7), 1, Priority(0), 1, task));
        Assert.True(PumpUntilCondition(
            dispatcher,
            () => workerStarted.IsSet,
            maximumWorkerStarts: 2));

        Assert.True(dispatcher.TryCancel(Key(7)));
        Assert.False(dispatcher.TryGetTaskState(Key(7), out _));
        Assert.True(dispatcher.TryEnqueue(
            Key(7), 2, Priority(0), 2, new CompleteImmediatelyTask("new", [])));

        PumpUntilIdle(dispatcher, maximumWorkerStarts: 2);
        Assert.Equal(1, dispatcher.CancelledTaskCount);
        Assert.Equal(1, dispatcher.CompletedTaskCount);
        Assert.Equal(0, dispatcher.ActiveTaskCount);

        // The old generation's cancellation outcome is emitted only after its
        // in-flight worker completion is collected. The replacement generation
        // may therefore complete first; assert identities, not async arrival order.
        var outcomes = new List<ChunkGenerationTaskOutcome>();
        while (dispatcher.TryDequeueOutcome(out var outcome))
            outcomes.Add(outcome);

        var cancelled = Assert.Single(
            outcomes,
            item => item.Kind == ChunkGenerationTaskOutcomeKind.Cancelled);
        var completed = Assert.Single(
            outcomes,
            item => item.Kind == ChunkGenerationTaskOutcomeKind.Completed);
        Assert.Equal(1UL, cancelled.Task.Generation);
        Assert.Equal(2UL, completed.Task.Generation);
        Assert.Empty(outcomes.Where(item =>
            item.Kind is not (ChunkGenerationTaskOutcomeKind.Cancelled or
                ChunkGenerationTaskOutcomeKind.Completed)));
    }

    [Fact]
    public async Task TaskAdvancesUseOneSerialLaneOnTheSharedWorkerPool()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(2, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var ownerThread = Environment.CurrentManagedThreadId;
        using var firstStarted = new ManualResetEventSlim(false);
        using var releaseFirst = new ManualResetEventSlim(false);
        var probe = new SerialLaneProbe();

        Assert.True(dispatcher.TryEnqueue(
            Key(20), 1, Priority(0), 20,
            new SerialLaneTask(probe, firstStarted, releaseFirst)));
        Assert.True(dispatcher.TryEnqueue(
            Key(21), 1, Priority(0), 21,
            new SerialLaneTask(probe, null, null)));

        var initial = dispatcher.Pump(GenerousBudget(), 2);
        Assert.Equal(1, initial.TasksAdvanced);
        Assert.True(firstStarted.Wait(TimeSpan.FromSeconds(5)));
        Assert.True(dispatcher.HasTaskAdvanceInFlight);

        // A spare physical worker must not allow a second advance for this
        // dimension while the first continuation still owns the serial lane.
        var whileBlocked = dispatcher.Pump(GenerousBudget(), 2);
        Assert.Equal(0, whileBlocked.TasksAdvanced);
        Assert.Equal(1, probe.AdvanceCount);

        // Cancellation invalidates the result but cannot dispose the task or
        // free the serial slot until its already-running advance returns.
        Assert.True(dispatcher.TryCancel(Key(20)));
        var whileCancelling = dispatcher.Pump(GenerousBudget(), 2);
        Assert.Equal(0, whileCancelling.TasksAdvanced);
        Assert.Equal(1, probe.AdvanceCount);

        releaseFirst.Set();
        PumpUntilIdle(dispatcher, maximumWorkerStarts: 4);

        Assert.Equal(2, probe.AdvanceCount);
        Assert.Equal(1, probe.MaximumConcurrentAdvances);
        Assert.Equal(1, dispatcher.CancelledTaskCount);
        Assert.Equal(1, dispatcher.CompletedTaskCount);
        Assert.All(probe.ThreadIds, threadId => Assert.NotEqual(ownerThread, threadId));
        Assert.False(dispatcher.HasTaskAdvanceInFlight);
    }

    [Fact]
    public async Task LightingLaneDoesNotBlockWorldgenLaneOnSharedPool()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(2, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        using var lightStarted = new ManualResetEventSlim(false);
        using var releaseLight = new ManualResetEventSlim(false);
        var lightTask = new LightLaneTask(lightStarted, releaseLight);
        var sharedOrder = new System.Collections.Concurrent.ConcurrentQueue<string>();

        Assert.True(dispatcher.TryEnqueue(
            Key(30), 1, Priority(0), 30, lightTask));
        Assert.True(PumpUntilCondition(
            dispatcher,
            () => lightStarted.IsSet && dispatcher.HasLightingOperationInFlight,
            maximumWorkerStarts: 4));

        Assert.True(dispatcher.TryEnqueue(
            Key(31), 1, Priority(1),
            31, new OrderedWorkerTask("shared-worker", sharedOrder)));

        // The light executor is occupied, but the other worker in the same
        // physical pool must remain available to ordinary worldgen forks.
        Assert.True(PumpUntilCondition(
            dispatcher,
            () => dispatcher.CompletedTaskCount == 1,
            maximumWorkerStarts: 4));
        Assert.Equal(new[] { "shared-worker" }, sharedOrder.ToArray());
        Assert.True(dispatcher.HasLightingOperationInFlight);

        releaseLight.Set();
        PumpUntilIdle(dispatcher, maximumWorkerStarts: 4);

        Assert.Equal(2, dispatcher.CompletedTaskCount);
        Assert.False(dispatcher.HasLightingOperationInFlight);
        Assert.False(dispatcher.HasTaskAdvanceInFlight);
        Assert.Equal(0, dispatcher.WaitingTaskCount);
    }

    [Fact]
    public async Task DispatcherPublishesMonotonicGenerationStatusOnOwnerThread()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var ownerThread = Environment.CurrentManagedThreadId;
        var observed = new List<(ChunkGenerationStatus Status, int ThreadId)>();
        dispatcher.GenerationStatusAdvanced += (_, status) =>
            observed.Add((status, Environment.CurrentManagedThreadId));

        Assert.True(dispatcher.TryEnqueue(
            Key(32), 1, Priority(0), 32, new StatusProgressTask()));

        PumpUntilIdle(dispatcher);

        Assert.Equal(
            new[]
            {
                ChunkGenerationStatus.Terrain,
                ChunkGenerationStatus.Features,
                ChunkGenerationStatus.InitializeLight,
                ChunkGenerationStatus.Light,
            },
            observed.Select(item => item.Status));
        Assert.All(observed, item => Assert.Equal(ownerThread, item.ThreadId));
        Assert.Equal(1, dispatcher.CompletedTaskCount);
    }

    [Fact]
    public void DispatcherRejectsAccessFromNonOwningThread()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        using var worker = new TestWorkerPoolScope(mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, worker.Pool);
        var result = Task.Run(() =>
            Assert.Throws<InvalidOperationException>(
                () => dispatcher.Pump(GenerousBudget(), 0)));

        Assert.True(result.Wait(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public async Task InvalidTaskRegistrationAndWorkerStartBudgetsAreRejected()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => dispatcher.TryEnqueue(
                Key(8), 0, Priority(0), 0, new CompleteImmediatelyTask("x", [])));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => dispatcher.Pump(GenerousBudget(), -1));
    }

    private static ChunkGenerationDispatchReport PumpUntilIdle(
        ChunkGenerationDispatcher dispatcher,
        int maximumWorkerStarts = 8)
    {
        var completions = 0;
        var stale = 0;
        var advanced = 0;
        var completed = 0;
        var failed = 0;
        var cancelled = 0;
        var starts = 0;

        var idle = SpinWait.SpinUntil(
            () =>
            {
                var report = dispatcher.Pump(
                    GenerousBudget(), maximumWorkerStarts);
                completions += report.CompletionsProcessed;
                stale += report.StaleCompletionsIgnored;
                advanced += report.TasksAdvanced;
                completed += report.TasksCompleted;
                failed += report.TasksFailed;
                cancelled += report.TasksCancelled;
                starts += report.WorkerOperationsStarted;

                return dispatcher.ActiveTaskCount == 0 &&
                    dispatcher.RetiringTaskCount == 0 &&
                    dispatcher.PendingWorkerOperationCount == 0 &&
                    !dispatcher.HasTaskAdvanceInFlight;
            },
            TimeSpan.FromSeconds(5));

        Assert.True(idle, "Generation dispatcher did not reach an idle state.");
        return new ChunkGenerationDispatchReport(
            completions, stale, advanced, completed, failed,
            cancelled, starts,
            dispatcher.ReadyTaskCount,
            dispatcher.WaitingTaskCount,
            dispatcher.PendingWorkerOperationCount,
            dispatcher.RetiringTaskCount);
    }

    private static bool PumpUntilCondition(
        ChunkGenerationDispatcher dispatcher,
        Func<bool> condition,
        int maximumWorkerStarts = 8) =>
        SpinWait.SpinUntil(
            () =>
            {
                dispatcher.Pump(GenerousBudget(), maximumWorkerStarts);
                return condition();
            },
            TimeSpan.FromSeconds(5));

    private static WorldFrameWorkBudget GenerousBudget() =>
        new(long.MaxValue);

    private static ChunkGenerationTaskKey Key(int x) =>
        new(new ChunkColumnCoord(x, 0), ChunkGenerationStepId.SectionReady);

    private static ChunkGenerationTaskPriority Priority(int ticket) =>
        new(ticket, ticket);

    private sealed class SerialLaneProbe
    {
        private int _activeAdvances;
        private int _maximumConcurrentAdvances;
        private int _advanceCount;

        public int AdvanceCount => Volatile.Read(ref _advanceCount);

        public int MaximumConcurrentAdvances =>
            Volatile.Read(ref _maximumConcurrentAdvances);

        public System.Collections.Concurrent.ConcurrentQueue<int> ThreadIds { get; } = new();

        public void Advance(
            CancellationToken cancellationToken,
            ManualResetEventSlim? started,
            ManualResetEventSlim? release)
        {
            var active = Interlocked.Increment(ref _activeAdvances);
            while (true)
            {
                var maximum = Volatile.Read(ref _maximumConcurrentAdvances);
                if (maximum >= active ||
                    Interlocked.CompareExchange(
                        ref _maximumConcurrentAdvances, active, maximum) == maximum)
                    break;
            }

            Interlocked.Increment(ref _advanceCount);
            ThreadIds.Enqueue(Environment.CurrentManagedThreadId);
            started?.Set();
            try
            {
                // Model an already-started CPU stage: cancellation invalidates
                // its result but does not preempt the work in the middle.
                release?.Wait();
                cancellationToken.ThrowIfCancellationRequested();
            }
            finally
            {
                Interlocked.Decrement(ref _activeAdvances);
            }
        }
    }

    private sealed class SerialLaneTask(
        SerialLaneProbe probe,
        ManualResetEventSlim? started,
        ManualResetEventSlim? release) : IChunkGenerationTask
    {
        public ChunkGenerationTaskAdvanceResult Advance(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
            CancellationToken cancellationToken)
        {
            probe.Advance(cancellationToken, started, release);
            return ChunkGenerationTaskAdvanceResult.Completed;
        }
    }

    private sealed record CompletedChunkPayload(string Id, int CellCount);

    private sealed class ResultTask(object payload) : IChunkGenerationTask
    {
        public ChunkGenerationTaskAdvanceResult Advance(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
            CancellationToken cancellationToken) =>
            ChunkGenerationTaskAdvanceResult.CompletedWith(payload);
    }

    private sealed class YieldOnceTask(string label, List<string> order) :
        IChunkGenerationTask
    {
        private int _calls;

        public ChunkGenerationTaskAdvanceResult Advance(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
            CancellationToken cancellationToken)
        {
            order.Add(label);
            return _calls++ == 0
                ? ChunkGenerationTaskAdvanceResult.Yield
                : ChunkGenerationTaskAdvanceResult.Completed;
        }
    }

    private sealed class CompleteImmediatelyTask(string label, List<string> order) :
        IChunkGenerationTask
    {
        public ChunkGenerationTaskAdvanceResult Advance(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
            CancellationToken cancellationToken)
        {
            order.Add(label);
            return ChunkGenerationTaskAdvanceResult.Completed;
        }
    }

    private sealed class TwoWorkerTask : IChunkGenerationTask
    {
        private bool _waiting;
        public List<string?> ObservedResults { get; } = [];

        public ChunkGenerationTaskAdvanceResult Advance(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
            CancellationToken cancellationToken)
        {
            if (!_waiting)
            {
                _waiting = true;
                return ChunkGenerationTaskAdvanceResult.WaitForWorkers(
                [
                    new ChunkGenerationWorkerOperation(
                        101, _ => "terrain-a"),
                    new ChunkGenerationWorkerOperation(
                        102, _ => "terrain-b"),
                ]);
            }

            ObservedResults.AddRange(
                completedDependencies.Select(item => item.Result as string));
            return ChunkGenerationTaskAdvanceResult.Completed;
        }
    }

    private sealed class StatusProgressTask : IChunkGenerationTask
    {
        private int _advance;

        public ChunkGenerationTaskAdvanceResult Advance(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return _advance++ switch
            {
                0 => ChunkGenerationTaskAdvanceResult.YieldWithStatus(
                    ChunkGenerationStatus.Terrain),
                1 => ChunkGenerationTaskAdvanceResult.YieldWithStatus(
                    ChunkGenerationStatus.Features),
                2 => ChunkGenerationTaskAdvanceResult.WaitForWorkers(
                    [
                        new ChunkGenerationWorkerOperation(
                            701,
                            _ => "light",
                            ChunkGenerationExecutionPolicy.LightExecutor),
                    ],
                    ChunkGenerationStatus.InitializeLight),
                3 => CompleteLight(completedDependencies),
                _ => throw new InvalidOperationException(
                    "Status progress task advanced beyond completion."),
            };
        }

        private static ChunkGenerationTaskAdvanceResult CompleteLight(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies)
        {
            Assert.Single(completedDependencies);
            Assert.Equal("light", completedDependencies[0].Result);
            return ChunkGenerationTaskAdvanceResult.CompletedWith(
                new object(),
                ChunkGenerationStatus.Light);
        }
    }

    private sealed class LightLaneTask(
        ManualResetEventSlim started,
        ManualResetEventSlim release) : IChunkGenerationTask
    {
        private bool _waiting;

        public ChunkGenerationTaskAdvanceResult Advance(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
            CancellationToken cancellationToken)
        {
            if (!_waiting)
            {
                _waiting = true;
                return ChunkGenerationTaskAdvanceResult.WaitForWorkers(
                [
                    new ChunkGenerationWorkerOperation(
                        501,
                        token =>
                        {
                            started.Set();
                            release.Wait();
                            token.ThrowIfCancellationRequested();
                            return "light";
                        },
                        ChunkGenerationExecutionPolicy.LightExecutor),
                ]);
            }

            Assert.Equal(1, completedDependencies.Count);
            Assert.Equal("light", completedDependencies[0].Result);
            return ChunkGenerationTaskAdvanceResult.Completed;
        }
    }

    private sealed class OrderedWorkerTask(
        string label,
        System.Collections.Concurrent.ConcurrentQueue<string> executionOrder) :
        IChunkGenerationTask
    {
        private bool _waiting;

        public ChunkGenerationTaskAdvanceResult Advance(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
            CancellationToken cancellationToken)
        {
            if (!_waiting)
            {
                _waiting = true;
                return ChunkGenerationTaskAdvanceResult.WaitForWorkers(
                [
                    new ChunkGenerationWorkerOperation(
                        401,
                        _ =>
                        {
                            executionOrder.Enqueue(label);
                            return label;
                        }),
                ]);
            }

            return ChunkGenerationTaskAdvanceResult.Completed;
        }
    }

    private sealed class FailingWorkerTask(Exception error) : IChunkGenerationTask
    {
        private bool _waiting;
        public int ResumeCount { get; private set; }

        public ChunkGenerationTaskAdvanceResult Advance(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
            CancellationToken cancellationToken)
        {
            if (!_waiting)
            {
                _waiting = true;
                return ChunkGenerationTaskAdvanceResult.WaitForWorkers(
                [
                    new ChunkGenerationWorkerOperation(201, _ => throw error),
                ]);
            }

            ResumeCount++;
            return ChunkGenerationTaskAdvanceResult.Completed;
        }
    }

    private sealed class BlockingWorkerTask(ManualResetEventSlim started) :
        IChunkGenerationTask
    {
        private bool _waiting;

        public ChunkGenerationTaskAdvanceResult Advance(
            IReadOnlyList<ChunkGenerationDependencyCompletion> completedDependencies,
            CancellationToken cancellationToken)
        {
            if (!_waiting)
            {
                _waiting = true;
                return ChunkGenerationTaskAdvanceResult.WaitForWorkers(
                [
                    new ChunkGenerationWorkerOperation(
                        301,
                        token =>
                        {
                            started.Set();
                            token.WaitHandle.WaitOne();
                            token.ThrowIfCancellationRequested();
                            return "should be cancelled";
                        }),
                ]);
            }

            return ChunkGenerationTaskAdvanceResult.Completed;
        }
    }

    private sealed class TestWorkerPoolScope : IDisposable
    {
        public TestWorkerPoolScope(ChunkGenerationCompletionMailbox mailbox) =>
            Pool = new ChunkGenerationWorkerPool(1, mailbox);

        public ChunkGenerationWorkerPool Pool { get; }

        public void Dispose() => Pool.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
