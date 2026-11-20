using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationCompletionMailboxTests
{
    [Fact]
    public void CompletionPreservesTaskGenerationWaitAndDependencyIdentity()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        var payload = new DetachedWorkerResult(42, "terrain-slice");
        var completion = Completion(4, result: payload);

        mailbox.Publish(completion);

        Assert.Equal(1, mailbox.PendingCount);
        Assert.True(mailbox.TryDequeue(out var dequeued));
        Assert.Equal(completion, dequeued);
        Assert.Same(payload, dequeued.Result);
        Assert.Null(dequeued.Error);
        Assert.Equal(0, mailbox.PendingCount);
    }

    [Fact]
    public void CompletionCanCarryWorkerFailureWithoutPayload()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();
        var error = new InvalidOperationException("worker failed");
        var completion = Completion(5, error: error);

        mailbox.Publish(completion);

        Assert.True(mailbox.TryDequeue(out var dequeued));
        Assert.Same(error, dequeued.Error);
        Assert.Null(dequeued.Result);
    }

    [Fact]
    public void CompletionRejectsZeroTaskWaitOrDependencyTokens()
    {
        var mailbox = new ChunkGenerationCompletionMailbox();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => mailbox.Publish(new ChunkGenerationDependencyCompletion(
                new ChunkGenerationTaskInstance(Key(), 0), 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => mailbox.Publish(new ChunkGenerationDependencyCompletion(
                new ChunkGenerationTaskInstance(Key(), 1), 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => mailbox.Publish(new ChunkGenerationDependencyCompletion(
                new ChunkGenerationTaskInstance(Key(), 1), 1, 0)));

        Assert.Equal(0, mailbox.PendingCount);
    }

    [Fact]
    public void ConcurrentProducersPublishEveryCompletionExactlyOnce()
    {
        const int completionCount = 4096;
        var mailbox = new ChunkGenerationCompletionMailbox();
        var completions = new ChunkGenerationDependencyCompletion[completionCount];

        Parallel.For(0, completionCount, index =>
        {
            var completion = Completion((ulong)(index + 1));
            mailbox.Publish(completion);
            completions[index] = completion;
        });

        Assert.Equal(completionCount, mailbox.PendingCount);
        var drained = new HashSet<ChunkGenerationDependencyCompletion>();
        while (mailbox.TryDequeue(out var completion))
            Assert.True(drained.Add(completion));

        Assert.Equal(completionCount, drained.Count);
        Assert.Equal(0, mailbox.PendingCount);
        Assert.Equal(completions.ToHashSet(), drained);
    }

    [Fact]
    public void ConcurrentProducerAndConsumerKeepPendingCountConsistent()
    {
        const int completionCount = 8192;
        var mailbox = new ChunkGenerationCompletionMailbox();
        var consumed = new System.Collections.Concurrent.ConcurrentBag<
            ChunkGenerationDependencyCompletion>();

        var producer = Task.Run(() =>
        {
            Parallel.For(0, completionCount, index =>
                mailbox.Publish(Completion((ulong)(index + 1))));
        });

        var consumer = Task.Run(async () =>
        {
            while (!producer.IsCompleted || mailbox.PendingCount > 0)
            {
                while (mailbox.TryDequeue(out var completion))
                    consumed.Add(completion);
                await Task.Yield();
            }
        });

        Task.WaitAll(producer, consumer);

        Assert.Equal(completionCount, consumed.Count);
        Assert.Equal(completionCount,
            consumed.Select(completion => completion.DependencyId).Distinct().Count());
        Assert.Equal(0, mailbox.PendingCount);
    }

    private sealed record DetachedWorkerResult(int CellsProcessed, string Stage);

    private static ChunkGenerationDependencyCompletion Completion(
        ulong dependencyId,
        Exception? error = null,
        object? result = null) =>
        new(
            new ChunkGenerationTaskInstance(Key(), Generation: 9),
            WaitToken: 7,
            DependencyId: dependencyId,
            Result: result,
            Error: error);

    private static ChunkGenerationTaskKey Key() =>
        new(
            new ChunkColumnCoord(4, -2),
            ChunkGenerationStepId.SectionReady);
}
