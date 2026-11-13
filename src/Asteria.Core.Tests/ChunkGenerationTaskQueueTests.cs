using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationTaskQueueTests
{
    [Fact]
    public void DequeueUsesTicketThenHorizontalDistance()
    {
        var queue = new ChunkGenerationTaskQueue();
        var fartherUrgent = Key(1, 0);
        var nearLessUrgent = Key(2, 0);
        var nearMostUrgent = Key(3, 0);

        queue.Enqueue(nearLessUrgent,
            new ChunkGenerationTaskPriority(1, 1), 10);
        queue.Enqueue(fartherUrgent,
            new ChunkGenerationTaskPriority(0, 400), 11);
        queue.Enqueue(nearMostUrgent,
            new ChunkGenerationTaskPriority(0, 1), 12);

        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal(nearMostUrgent, first.Key);
        Assert.True(queue.TryDequeue(out var second));
        Assert.Equal(fartherUrgent, second.Key);
        Assert.True(queue.TryDequeue(out var third));
        Assert.Equal(nearLessUrgent, third.Key);
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void PriorityUpdateReordersEntryAndPreservesAgeAndSequence()
    {
        var queue = new ChunkGenerationTaskQueue();
        var older = Key(1, 0);
        var newer = Key(2, 0);
        queue.Enqueue(older, new ChunkGenerationTaskPriority(2, 100), 1234);
        queue.Enqueue(newer, new ChunkGenerationTaskPriority(1, 1), 5678);
        Assert.True(queue.TryGet(older, out var before));

        Assert.True(queue.UpdatePriority(
            older, new ChunkGenerationTaskPriority(0, 1)));
        Assert.True(queue.TryGet(older, out var after));

        Assert.Equal(before.EnqueuedTimestamp, after.EnqueuedTimestamp);
        Assert.Equal(before.EnqueueSequence, after.EnqueueSequence);
        Assert.True(queue.TryDequeue(out var first));
        Assert.Equal(older, first.Key);
    }

    [Fact]
    public void DuplicateEnqueueDoesNotReplaceExistingRequest()
    {
        var queue = new ChunkGenerationTaskQueue();
        var key = Key(4, 2);
        var originalPriority = new ChunkGenerationTaskPriority(2, 25);
        Assert.True(queue.Enqueue(key, originalPriority, 100));

        Assert.False(queue.Enqueue(
            key, new ChunkGenerationTaskPriority(0, 0), 200));

        Assert.Equal(1, queue.Count);
        Assert.True(queue.TryGet(key, out var entry));
        Assert.Equal(originalPriority, entry.Priority);
        Assert.Equal(100, entry.EnqueuedTimestamp);
    }

    [Fact]
    public void EqualPriorityUsesStableFifoEnqueueSequence()
    {
        var queue = new ChunkGenerationTaskQueue();
        var first = Key(5, 0);
        var second = Key(4, 0);
        var third = Key(3, 0);
        var priority = new ChunkGenerationTaskPriority(1, 9);

        queue.Enqueue(first, priority, 30);
        queue.Enqueue(second, priority, 10);
        queue.Enqueue(third, priority, 20);

        Assert.True(queue.TryDequeue(out var firstEntry));
        Assert.Equal(first, firstEntry.Key);
        Assert.True(queue.TryDequeue(out var secondEntry));
        Assert.Equal(second, secondEntry.Key);
        Assert.True(queue.TryDequeue(out var thirdEntry));
        Assert.Equal(third, thirdEntry.Key);
    }

    [Fact]
    public void RemoveAndClearKeepIndexesConsistent()
    {
        var queue = new ChunkGenerationTaskQueue();
        var first = Key(6, 0);
        var second = Key(7, 1);
        queue.Enqueue(first, new ChunkGenerationTaskPriority(0, 0), 1);
        queue.Enqueue(second, new ChunkGenerationTaskPriority(0, 1), 2);

        Assert.True(queue.Remove(first));
        Assert.False(queue.Remove(first));
        Assert.False(queue.Contains(first));
        Assert.True(queue.Contains(second));
        Assert.Equal(1, queue.Count);

        queue.Clear();
        Assert.True(queue.IsEmpty);
        Assert.False(queue.TryPeek(out _));
        Assert.False(queue.TryDequeue(out _));
    }

    [Fact]
    public void MissingTaskCannotBeReprioritized()
    {
        var queue = new ChunkGenerationTaskQueue();

        Assert.False(queue.UpdatePriority(
            Key(9, 1), new ChunkGenerationTaskPriority(0, 0)));
    }

    private static ChunkGenerationTaskKey Key(int x, int z) =>
        new(
            new ChunkColumnCoord(x, z),
            ChunkGenerationStepId.SectionReady);
}
