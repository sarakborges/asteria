using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationDispatcherTests
{
    [Fact]
    public async Task YieldedTaskReturnsBehindPeersAtEqualPriority()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(2);
        await using var pool = new ChunkGenerationWorkerPool(1, 1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var order = new List<string>();
        var first = new YieldOnceTask("first", order);
        var second = new CompleteImmediatelyTask("second", order);

        Assert.True(dispatcher.TryEnqueue(Key(1), 1, Priority(0), 1, first));
        Assert.True(dispatcher.TryEnqueue(Key(2), 1, Priority(0), 2, second));

        var firstPump = dispatcher.Pump(GenerousBudget(), 0);

        Assert.Equal(3, firstPump.TasksAdvanced);
        Assert.Equal(2, firstPump.TasksCompleted);
        Assert.Equal(new[] { "first", "second", "first" }, order);
        Assert.Equal(0, dispatcher.ActiveTaskCount);
        Assert.Equal(2, dispatcher.CompletedTaskCount);

        var secondPump = dispatcher.Pump(GenerousBudget(), 0);

        Assert.Equal(0, secondPump.TasksAdvanced);
        Assert.Equal(2, dispatcher.CompletedTaskCount);
    }

    [Fact]
    public async Task RequeuedTerminalOutcomeRemainsAvailableExactlyOnce()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(1);
        await using var pool = new ChunkGenerationWorkerPool(1, 1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var payload = new CompletedChunkPayload("deferred", 16);
        Assert.True(dispatcher.TryEnqueue(
            Key(11), 4, Priority(0), 11, new ResultTask(payload)));

        dispatcher.Pump(GenerousBudget(), 0);

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
        var mailbox = new ChunkGenerationCompletionMailbox(1);
        await using var pool = new ChunkGenerationWorkerPool(1, 1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var payload = new CompletedChunkPayload("chunk-10", 4096);

        Assert.True(dispatcher.TryEnqueue(
            Key(10), 3, Priority(0), 10, new ResultTask(payload)));
        dispatcher.Pump(GenerousBudget(), 0);

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
        var mailbox = new ChunkGenerationCompletionMailbox(4);
        await using var pool = new ChunkGenerationWorkerPool(2, 4, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var task = new TwoWorkerTask();
        Assert.True(dispatcher.TryEnqueue(Key(3), 8, Priority(0), 10, task));

        var initial = dispatcher.Pump(GenerousBudget(), 2);

        Assert.Equal(1, initial.TasksAdvanced);
        Assert.Equal(2, initial.WorkerOperationsStarted);
        Assert.Equal(1, dispatcher.WaitingTaskCount);
        Assert.True(dispatcher.TryGetTaskState(Key(3), out var waitingState));
        Assert.Equal(ChunkGenerationTaskState.WaitingForDependencies, waitingState);

        ChunkGenerationDispatchReport final = default;
        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                final = dispatcher.Pump(GenerousBudget(), 2);
                return dispatcher.ActiveTaskCount == 0;
            },
            TimeSpan.FromSeconds(5)));

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
        var mailbox = new ChunkGenerationCompletionMailbox(2);
        await using var pool = new ChunkGenerationWorkerPool(1, 1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var order = new List<string>();

        Assert.True(dispatcher.TryEnqueue(
            Key(4), 1, Priority(3), 1, new CompleteImmediatelyTask("far", order)));
        Assert.True(dispatcher.TryEnqueue(
            Key(5), 1, Priority(2), 2, new CompleteImmediatelyTask("near", order)));
        Assert.True(dispatcher.UpdatePriority(Key(4), Priority(0)));

        var report = dispatcher.Pump(GenerousBudget(), 0);

        Assert.Equal(new[] { "far", "near" }, order);
        Assert.Equal(2, report.TasksCompleted);
        Assert.Equal(0, dispatcher.ActiveTaskCount);
    }

    [Fact]
    public async Task WorkerFailureFailsTaskWithoutResumingIt()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(2);
        await using var pool = new ChunkGenerationWorkerPool(1, 2, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        var error = new InvalidOperationException("worker slice failed");
        var task = new FailingWorkerTask(error);
        Assert.True(dispatcher.TryEnqueue(Key(6), 1, Priority(0), 1, task));

        dispatcher.Pump(GenerousBudget(), 1);

        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                dispatcher.Pump(GenerousBudget(), 1);
                return dispatcher.ActiveTaskCount == 0;
            },
            TimeSpan.FromSeconds(5)));

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
        var mailbox = new ChunkGenerationCompletionMailbox(4);
        await using var pool = new ChunkGenerationWorkerPool(1, 2, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);
        using var workerStarted = new ManualResetEventSlim(false);

        var task = new BlockingWorkerTask(workerStarted);
        Assert.True(dispatcher.TryEnqueue(Key(7), 1, Priority(0), 1, task));
        dispatcher.Pump(GenerousBudget(), 1);
        Assert.True(workerStarted.Wait(TimeSpan.FromSeconds(5)));

        Assert.True(dispatcher.TryCancel(Key(7)));
        Assert.False(dispatcher.TryGetTaskState(Key(7), out _));
        Assert.True(dispatcher.TryEnqueue(
            Key(7), 2, Priority(0), 2, new CompleteImmediatelyTask("new", [])));

        Assert.True(SpinWait.SpinUntil(
            () =>
            {
                dispatcher.Pump(GenerousBudget(), 1);
                return dispatcher.RetiringTaskCount == 0;
            },
            TimeSpan.FromSeconds(5)));
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
    public void DispatcherRejectsAccessFromNonOwningThread()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(1);
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
        var mailbox = new ChunkGenerationCompletionMailbox(1);
        await using var pool = new ChunkGenerationWorkerPool(1, 1, mailbox);
        var dispatcher = new ChunkGenerationDispatcher(mailbox, pool);

        Assert.Throws<ArgumentOutOfRangeException>(
            () => dispatcher.TryEnqueue(
                Key(8), 0, Priority(0), 0, new CompleteImmediatelyTask("x", [])));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => dispatcher.Pump(GenerousBudget(), -1));
    }

    private static WorldFrameWorkBudget GenerousBudget() =>
        new(long.MaxValue);

    private static ChunkGenerationTaskKey Key(int x) =>
        new(new ChunkColumnCoord(x, 0), ChunkGenerationStepId.SectionReady);

    private static ChunkGenerationTaskPriority Priority(int ticket) =>
        new(ticket, ticket);

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
            Pool = new ChunkGenerationWorkerPool(1, 1, mailbox);

        public ChunkGenerationWorkerPool Pool { get; }

        public void Dispose() => Pool.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
