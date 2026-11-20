using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkResidencySettingsTests
{
    [Fact]
    public void SettingsExposePhysicalWorkerCapacityAndEvictionBudget()
    {
        var settings = CreateSettings(
            maxConcurrentWorkers: 3,
            maxEvictionsPerFrame: 2);

        Assert.Equal(3, settings.MaxConcurrentWorkers);
        Assert.Equal(2, settings.MaxEvictionsPerFrame);
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(-1, 2)]
    [InlineData(2, 0)]
    [InlineData(2, -1)]
    public void NonPositiveWorkerAndEvictionBudgetsAreRejected(
        int workers,
        int evictions)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateSettings(workers, evictions));
    }

    private static ChunkResidencySettings CreateSettings(
        int maxConcurrentWorkers = 2,
        int maxEvictionsPerFrame = 2) =>
        new(
            maxConcurrentWorkers,
            maxEvictionsPerFrame,
            new WorldGameRules());
}
