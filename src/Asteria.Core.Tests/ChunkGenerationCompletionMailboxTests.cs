using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationCompletionMailboxTests
{
    [Fact]
    public void CapacityMustBePositive()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChunkGenerationCompletionMailbox(0));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ChunkGenerationCompletionMailbox(-1));
    }

    [Fact]
    public void ReservationBoundsPendingAndInFlightCompletions()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(capacity: 2);

        Assert.True(mailbox.TryReserve(out var first));
        Assert.True(mailbox.TryReserve(out var second));
        Assert.False(mailbox.TryReserve(out var third));
        Assert.Null(third);
        Assert.Equal(0, mailbox.AvailableCapacity);
        Assert.Equal(2, mailbox.ReservedOrPendingCount);

        Assert.True(first!.TryPublish(Completion(1)));
        Assert.Equal(1, mailbox.PendingCount);
        Assert.Equal(2, mailbox.ReservedOrPendingCount);

        // The published completion still occupies its slot. Releasing the
        // unused second reservation makes exactly one slot available.
        second!.Dispose();
        Assert.True(mailbox.TryReserve(out third));
        Assert.Equal(0, mailbox.AvailableCapacity);

        Assert.True(mailbox.TryDequeue(out var dequeued));
        Assert.Equal(Completion(1), dequeued);
        Assert.Equal(1, mailbox.ReservedOrPendingCount);

        third!.Dispose();
        Assert.Equal(0, mailbox.ReservedOrPendingCount);
        Assert.Equal(2, mailbox.AvailableCapacity);
    }

    [Fact]
    public void ReservationCanPublishAtMostOnce()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(capacity: 1);
        Assert.True(mailbox.TryReserve(out var reservation));

        Assert.True(reservation!.TryPublish(Completion(2)));
        Assert.False(reservation.TryPublish(Completion(3)));
        reservation.Dispose();

        Assert.Equal(1, mailbox.PendingCount);
        Assert.Equal(1, mailbox.ReservedOrPendingCount);
        Assert.True(mailbox.TryDequeue(out var completion));
        Assert.Equal(Completion(2), completion);
        Assert.Equal(0, mailbox.ReservedOrPendingCount);
    }

    [Fact]
    public void DisposingUnusedReservationReturnsCapacityWithoutPublishing()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(capacity: 1);
        Assert.True(mailbox.TryReserve(out var reservation));

        reservation!.Dispose();

        Assert.Equal(0, mailbox.PendingCount);
        Assert.Equal(0, mailbox.ReservedOrPendingCount);
        Assert.True(mailbox.TryReserve(out _));
    }

    [Fact]
    public void CompletionPreservesTaskGenerationWaitAndDependencyIdentity()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(capacity: 1);
        var payload = new DetachedWorkerResult(42, "terrain-slice");
        Assert.True(mailbox.TryReserve(out var reservation));
        var completion = Completion(4, result: payload);

        Assert.True(reservation!.TryPublish(completion));
        Assert.True(mailbox.TryDequeue(out var dequeued));
        Assert.Equal(completion, dequeued);
        Assert.Same(payload, dequeued.Result);
        Assert.Null(dequeued.Error);
    }

    [Fact]
    public void CompletionCanCarryWorkerFailureWithoutPayload()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(capacity: 1);
        var error = new InvalidOperationException("worker failed");
        Assert.True(mailbox.TryReserve(out var reservation));
        var completion = Completion(5, error: error);

        Assert.True(reservation!.TryPublish(completion));
        Assert.True(mailbox.TryDequeue(out var dequeued));
        Assert.Same(error, dequeued.Error);
        Assert.Null(dequeued.Result);
    }

    [Fact]
    public void CompletionRejectsZeroTaskWaitOrDependencyTokens()
    {
        var mailbox = new ChunkGenerationCompletionMailbox(capacity: 1);
        Assert.True(mailbox.TryReserve(out var reservation));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => reservation!.TryPublish(new ChunkGenerationDependencyCompletion(
                new ChunkGenerationTaskInstance(Key(), 0), 1, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => reservation!.TryPublish(new ChunkGenerationDependencyCompletion(
                new ChunkGenerationTaskInstance(Key(), 1), 0, 1)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => reservation!.TryPublish(new ChunkGenerationDependencyCompletion(
                new ChunkGenerationTaskInstance(Key(), 1), 1, 0)));

        Assert.Equal(1, mailbox.ReservedOrPendingCount);
        reservation!.Dispose();
    }

    [Fact]
    public void ConcurrentProducersPublishEveryReservedCompletionExactlyOnce()
    {
        const int completionCount = 512;
        var mailbox = new ChunkGenerationCompletionMailbox(completionCount);
        var completions = new ChunkGenerationDependencyCompletion[completionCount];

        Parallel.For(0, completionCount, index =>
        {
            if (!mailbox.TryReserve(out var reservation))
                throw new InvalidOperationException("A pre-sized mailbox rejected a reservation.");

            var completion = Completion((ulong)(index + 1));
            if (!reservation!.TryPublish(completion))
                throw new InvalidOperationException("A fresh reservation rejected publication.");

            completions[index] = completion;
        });

        var drained = new HashSet<ChunkGenerationDependencyCompletion>();
        while (mailbox.TryDequeue(out var completion))
            Assert.True(drained.Add(completion));

        Assert.Equal(completionCount, drained.Count);
        Assert.Equal(0, mailbox.PendingCount);
        Assert.Equal(0, mailbox.ReservedOrPendingCount);
        Assert.Equal(completions.ToHashSet(), drained);
    }

    [Fact]
    public void ConcurrentReservationNeverExceedsCapacity()
    {
        const int capacity = 32;
        var mailbox = new ChunkGenerationCompletionMailbox(capacity);
        var accepted = 0;
        var reservations = new System.Collections.Concurrent.ConcurrentBag<
            ChunkGenerationCompletionReservation>();

        Parallel.For(0, 2048, _ =>
        {
            if (!mailbox.TryReserve(out var reservation))
                return;

            Interlocked.Increment(ref accepted);
            reservations.Add(reservation!);
        });

        Assert.Equal(capacity, accepted);
        Assert.Equal(capacity, mailbox.ReservedOrPendingCount);
        foreach (var reservation in reservations)
            reservation.Dispose();

        Assert.Equal(0, mailbox.ReservedOrPendingCount);
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
