using Asteria.Core.World;
using Xunit;

namespace Asteria.Core.Tests;

public sealed class ChunkGenerationCache2DTests
{
    [Fact]
    public void FullStepRetainsTheMinecraftRadiusElevenNeighborhood()
    {
        var manager = new ChunkDistanceManager();
        var center = new ChunkColumnCoord(10, -4);
        var step = ChunkPyramid.Generation.GetStep(ChunkStatus.Full);

        using var cache = manager.AcquireGenerationCache2D(center, step);

        Assert.Equal(11, cache.Radius);
        Assert.Equal(23 * 23, cache.Count);
        Assert.Equal(23 * 23, manager.HolderCount);
        Assert.Same(manager.GetHolder(center), cache.GetHolder(center));
        Assert.Equal(1, cache.GetHolder(center).DependencyClaimCount);
        Assert.Equal(
            step.AccumulatedDependencies.GetRequiredStatus(0),
            cache.GetRequiredStatus(center));
        Assert.Equal(
            step.AccumulatedDependencies.GetRequiredStatus(11),
            cache.GetRequiredStatus(center.X + 11, center.Z - 11));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            cache.GetHolder(center.X + 12, center.Z));
    }

    [Fact]
    public void EmptyStepRetainsOnlyItsTargetColumn()
    {
        var manager = new ChunkDistanceManager();
        var center = new ChunkColumnCoord(-7, 12);
        var step = ChunkPyramid.Generation.GetStep(ChunkStatus.Empty);

        using var cache = manager.AcquireGenerationCache2D(center, step);

        Assert.Equal(0, cache.Radius);
        Assert.Equal(1, cache.Count);
        Assert.Same(manager.GetHolder(center), cache.GetHolder(center));
        Assert.Null(cache.GetRequiredStatus(center));
    }

    [Fact]
    public void DisposingCacheReleasesEveryHolderClaimExactlyOnce()
    {
        var manager = new ChunkDistanceManager();
        var center = new ChunkColumnCoord(3, -9);
        var cache = manager.AcquireGenerationCache2D(
            center,
            ChunkPyramid.Generation.GetStep(ChunkStatus.Full));
        var columns = cache.Columns.ToArray();
        var holders = columns.Select(column => manager.GetHolder(column)!).ToArray();

        Assert.All(holders, holder => Assert.Equal(1, holder.DependencyClaimCount));

        cache.Dispose();
        cache.Dispose();

        Assert.All(holders, holder => Assert.Equal(0, holder.DependencyClaimCount));
        Assert.All(columns, column =>
            Assert.True(manager.CompletePendingHolderUnload(column)));
        Assert.Equal(0, manager.HolderCount);
        Assert.Throws<ObjectDisposedException>(() => cache.GetHolder(center));
    }
}
