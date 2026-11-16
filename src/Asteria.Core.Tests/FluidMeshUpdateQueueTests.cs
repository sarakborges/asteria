using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FluidMeshUpdateQueueTests
{
    [Fact]
    public void NearestDrainUsesStreamingPriorityInsteadOfCoordinateOrder()
    {
        var queue = new FluidMeshUpdateQueue();
        var distant = new ChunkCoord(-20, 0, 0);
        var nearby = new ChunkCoord(10, 0, 9);
        queue.EnqueueMeshlets(distant, ChunkMeshletMask.Single(0));
        queue.EnqueueMeshlets(nearby, ChunkMeshletMask.Single(1));

        var first = queue.Drain(
            maximumMeshlets: 1, center: new ChunkCoord(10, 0, 10));
        Assert.Equal(nearby, Assert.Single(first.DirtyMeshlets).Key);
        Assert.Equal(distant, Assert.Single(queue.Drain().DirtyMeshlets).Key);
    }

    [Fact]
    public void NearestDrainUsesMovementDirectionAndStableTieBreakers()
    {
        var queue = new FluidMeshUpdateQueue();
        var left = new ChunkCoord(-1, 0, 0);
        var right = new ChunkCoord(1, 0, 0);
        queue.EnqueueMeshlets(left, ChunkMeshletMask.Single(0));
        queue.EnqueueMeshlets(right, ChunkMeshletMask.Single(0));

        Assert.Equal(right, Assert.Single(queue.Drain(
            1, ChunkCoord.Zero, new ChunkMovementDirection(1, 0))
            .DirtyMeshlets).Key);
        Assert.Equal(left, Assert.Single(queue.Drain(1, ChunkCoord.Zero)
            .DirtyMeshlets).Key);
    }

    [Fact]
    public void NearestDrainStillHonorsPartialMeshletBudget()
    {
        var queue = new FluidMeshUpdateQueue();
        var near = new ChunkCoord(2, 0, 1);
        queue.EnqueueMeshlets(near, ChunkMeshletMask.All);
        var first = queue.Drain(2, new ChunkCoord(2, 0, 0));
        Assert.Equal(2, first.DirtyMeshlets[near].SelectedCount);
        Assert.Equal(ChunkMeshletMask.Count - 2,
            queue.Drain().DirtyMeshlets[near].SelectedCount);
        Assert.False(queue.HasWork);
    }

    [Fact]
    public void DrainHonorsMeshletLimitAndPreservesRemainingWork()
    {
        var queue =
            new FluidMeshUpdateQueue();

        queue.EnqueueMeshlets(
            ChunkCoord.Zero,
            ChunkMeshletMask.All);

        var first =
            queue.Drain(
                maximumMeshlets: 3);

        Assert.Equal(
            3,
            first.DirtyMeshlets
                .Values
                .Sum(mask =>
                    mask.SelectedCount));
        Assert.True(queue.HasWork);

        var second =
            queue.Drain();

        Assert.Equal(
            ChunkMeshletMask.Count - 3,
            second.DirtyMeshlets
                .Values
                .Sum(mask =>
                    mask.SelectedCount));
        Assert.False(queue.HasWork);
    }

    [Fact]
    public void DrainUsesDeterministicChunkAndMeshletOrder()
    {
        var queue =
            new FluidMeshUpdateQueue();

        queue.EnqueueMeshlets(
            new ChunkCoord(1, 0, 0),
            ChunkMeshletMask.Single(0));
        queue.EnqueueMeshlets(
            new ChunkCoord(0, 0, 0),
            ChunkMeshletMask.Single(1));
        queue.EnqueueMeshlets(
            new ChunkCoord(0, 0, 0),
            ChunkMeshletMask.Single(0));

        var first =
            queue.Drain(
                maximumMeshlets: 1);
        var firstEntry =
            Assert.Single(
                first.DirtyMeshlets);

        Assert.Equal(
            ChunkCoord.Zero,
            firstEntry.Key);
        Assert.Equal(
            ChunkMeshletMask.Single(0),
            firstEntry.Value);

        var second =
            queue.Drain(
                maximumMeshlets: 1);
        var secondEntry =
            Assert.Single(
                second.DirtyMeshlets);

        Assert.Equal(
            ChunkCoord.Zero,
            secondEntry.Key);
        Assert.Equal(
            ChunkMeshletMask.Single(1),
            secondEntry.Value);
        Assert.True(
            queue.HasWork);

        var third =
            queue.Drain(
                maximumMeshlets: 1);
        var thirdEntry =
            Assert.Single(
                third.DirtyMeshlets);

        Assert.Equal(
            new ChunkCoord(1, 0, 0),
            thirdEntry.Key);
        Assert.Equal(
            ChunkMeshletMask.Single(0),
            thirdEntry.Value);
        Assert.False(
            queue.HasWork);
    }
}
