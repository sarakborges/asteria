using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class FluidMeshUpdateQueueTests
{
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
            new ChunkCoord(1, 0, 0),
            secondEntry.Key);
        Assert.Equal(
            ChunkMeshletMask.Single(0),
            secondEntry.Value);
        Assert.False(
            queue.HasWork);
    }
}
