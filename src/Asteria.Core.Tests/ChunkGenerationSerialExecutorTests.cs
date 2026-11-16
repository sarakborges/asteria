using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationSerialExecutorTests
{
    [Fact]
    public async Task ExternalDependencyMailboxSharesSlotWithoutBeingConsumedByDispatcher()
    {
        var dispatcherMailbox = new ChunkGenerationCompletionMailbox();
        var externalMailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(2, dispatcherMailbox);
        var executor = new ChunkGenerationSerialExecutor(pool);

        var externalTask = TaskInstance(10, ChunkGenerationStepId.InitialLighting);
        var externalRequest = Request(
            externalTask,
            dependencyId: 71,
            _ => "dynamic-light");

        Assert.True(executor.TryScheduleDependency(
            externalRequest, externalMailbox));
        Assert.True(executor.HasInFlightTask);

        // The dispatcher's regular mailbox cannot consume an operation that
        // belongs to dynamic relighting, even though both share one executor.
        Assert.False(executor.TryCollectDependency(out _));
        Assert.False(executor.TryScheduleDependency(
            Request(TaskInstance(11, ChunkGenerationStepId.InitialLighting),
                72, _ => "second-light")));

        Assert.True(SpinWait.SpinUntil(
            () => externalMailbox.PendingCount == 1,
            TimeSpan.FromSeconds(5)));
        Assert.False(executor.TryCollectDependency(
            dispatcherMailbox, out _));
        Assert.True(executor.HasInFlightTask);

        Assert.True(executor.TryCollectDependency(
            externalMailbox, out var completion));
        Assert.Equal(externalTask, completion.Task);
        Assert.Equal(71UL, completion.DependencyId);
        Assert.Equal("dynamic-light", completion.Result);
        Assert.Null(completion.Error);
        Assert.False(executor.HasInFlightTask);
    }

    [Fact]
    public async Task SerialExecutorValidatesAndReleasesExternalMailboxCompletion()
    {
        var dispatcherMailbox = new ChunkGenerationCompletionMailbox();
        var externalMailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(2, dispatcherMailbox);
        var executor = new ChunkGenerationSerialExecutor(pool);
        var task = TaskInstance(20, ChunkGenerationStepId.InitialLighting);

        Assert.True(executor.TryScheduleDependency(
            Request(task, 91, _ => "sky-block-light"),
            externalMailbox));
        Assert.True(executor.HasInFlightTask);
        Assert.False(executor.TryCollectDependency(
            dispatcherMailbox, out _));

        ChunkGenerationDependencyCompletion completion = default;
        Assert.True(SpinWait.SpinUntil(
            () => executor.TryCollectDependency(
                externalMailbox, out completion),
            TimeSpan.FromSeconds(5)));
        Assert.Equal(task, completion.Task);
        Assert.Equal(1UL, completion.WaitToken);
        Assert.Equal(91UL, completion.DependencyId);
        Assert.Equal("sky-block-light", completion.Result);
        Assert.Null(completion.Error);
        Assert.False(executor.HasInFlightTask);
    }

    [Fact]
    public async Task TaskAdvanceAndDependencyCannotOccupySameSerialLane()
    {
        var dispatcherMailbox = new ChunkGenerationCompletionMailbox();
        var externalMailbox = new ChunkGenerationCompletionMailbox();
        await using var pool = new ChunkGenerationWorkerPool(2, dispatcherMailbox);
        var executor = new ChunkGenerationSerialExecutor(pool);
        var task = TaskInstance(30, ChunkGenerationStepId.SectionReady);

        Assert.True(executor.TrySchedule(
            task,
            _ => ChunkGenerationTaskAdvanceResult.Completed,
            CancellationToken.None,
            WorldWorkerPriority.FromPriorityClass(0)));
        Assert.False(executor.TryScheduleDependency(
            Request(TaskInstance(31, ChunkGenerationStepId.InitialLighting),
                101, _ => "blocked"),
            externalMailbox));

        ChunkGenerationTaskAdvanceCompletion completion = default;
        Assert.True(SpinWait.SpinUntil(
            () => executor.TryCollect(out completion),
            TimeSpan.FromSeconds(5)));
        Assert.Equal(task, completion.Task);
        Assert.Null(completion.Error);
        Assert.Equal(
            ChunkGenerationTaskAdvanceKind.Completed,
            completion.Result?.Kind);
        Assert.False(executor.HasInFlightTask);
    }

    private static ChunkGenerationWorkerRequest Request(
        ChunkGenerationTaskInstance task,
        ulong dependencyId,
        Func<CancellationToken, object?> execute) =>
        new(
            task,
            WaitToken: 1,
            DependencyId: dependencyId,
            Execute: execute);

    private static ChunkGenerationTaskInstance TaskInstance(
        int columnX,
        ChunkGenerationStepId step) =>
        new(
            new ChunkGenerationTaskKey(
                new ChunkColumnCoord(columnX, 0),
                step),
            Generation: 1);
}
