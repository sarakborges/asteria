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
    public void ConditionalPrefixDrainLeavesRejectedAndFollowingItemsInOrder()
    {
        var queue = new DeduplicatedQueue<int>();
        foreach (var item in new[] { 1, 3, 6, 8, 10 })
            queue.Enqueue(item);

        var revision = queue.Revision;
        Assert.Empty(queue.DrainPrefix(4, value => value < 0));
        Assert.Equal(revision, queue.Revision);
        Assert.Equal(new[] { 1, 3 }, queue.DrainPrefix(4, value => value < 6));
        Assert.Equal(new[] { 6, 8 }, queue.DrainPrefix(2, _ => true));
        Assert.Equal(new[] { 10 }, queue.Drain());
        Assert.Equal(0, queue.Count);
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
