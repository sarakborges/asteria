using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class DeduplicatedQueueTests
{
    [Fact]
    public void EnqueueDeduplicatesAndPreservesFifoOrder()
    {
        var queue =
            new DeduplicatedQueue<int>();

        Assert.True(queue.Enqueue(2));
        Assert.True(queue.Enqueue(1));
        Assert.False(queue.Enqueue(2));

        Assert.Equal(
            new[] { 2, 1 },
            queue.Drain());
        Assert.Equal(0, queue.Count);
    }

    [Fact]
    public void PriorityPromotionMovesExistingValueWithoutDuplication()
    {
        var queue =
            new DeduplicatedQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);
        queue.Enqueue(3);

        Assert.True(
            queue.EnqueueFront(2));

        Assert.Equal(
            new[] { 2, 1, 3 },
            queue.Drain());
    }

    [Fact]
    public void RemovalAndDrainKeepMembershipBounded()
    {
        var queue =
            new DeduplicatedQueue<int>();
        queue.Enqueue(1);
        queue.Enqueue(2);

        Assert.True(queue.Remove(1));
        Assert.False(queue.Contains(1));
        Assert.Equal(1, queue.Count);
        Assert.True(
            queue.TryDequeue(out var remaining));
        Assert.Equal(2, remaining);
        Assert.False(
            queue.TryDequeue(out _));
    }
}
