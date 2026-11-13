using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkResidencySettingsTests
{
    [Fact]
    public void SettingsExposePhysicalWorkerCapacityAndResultBudgets()
    {
        var settings = CreateSettings(
            maxConcurrentWorkers: 3,
            maxResultsPerFrame: 8,
            maxEvictionsPerFrame: 2);

        Assert.Equal(3, settings.MaxConcurrentWorkers);
        Assert.Equal(8, settings.MaxResultsPerFrame);
        Assert.Equal(2, settings.MaxEvictionsPerFrame);
    }

    [Theory]
    [InlineData(0, 8, 2)]
    [InlineData(-1, 8, 2)]
    [InlineData(2, 0, 2)]
    [InlineData(2, -1, 2)]
    [InlineData(2, 8, 0)]
    [InlineData(2, 8, -1)]
    public void NonPositiveWorkerAndPublicationBudgetsAreRejected(
        int workers,
        int results,
        int evictions)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateSettings(workers, results, evictions));
    }

    private static ChunkResidencySettings CreateSettings(
        int maxConcurrentWorkers = 2,
        int maxResultsPerFrame = 8,
        int maxEvictionsPerFrame = 2) =>
        new(
            maxConcurrentWorkers,
            maxResultsPerFrame,
            maxEvictionsPerFrame,
            new WorldGameRules());
}
