using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkResidencySettingsTests
{
    [Fact]
    public void GenerationContinuationBudgetsDefaultFromPhysicalWorkerCapacity()
    {
        var settings = CreateSettings(maxConcurrentWorkers: 3);

        Assert.Equal(3, settings.MaxConcurrentWorkers);
        Assert.Equal(8, settings.MaxResultsPerFrame);
        Assert.Equal(24, settings.MaxGenerationCompletionsPerFrame);
        Assert.Equal(24, settings.MaxGenerationTaskAdvancesPerFrame);
    }

    [Fact]
    public void GenerationContinuationBudgetsCanBeConfiguredIndependently()
    {
        var settings = CreateSettings(
            maxConcurrentWorkers: 4,
            maxResultsPerFrame: 3,
            maxGenerationCompletionsPerFrame: 19,
            maxGenerationTaskAdvancesPerFrame: 27);

        Assert.Equal(4, settings.MaxConcurrentWorkers);
        Assert.Equal(3, settings.MaxResultsPerFrame);
        Assert.Equal(19, settings.MaxGenerationCompletionsPerFrame);
        Assert.Equal(27, settings.MaxGenerationTaskAdvancesPerFrame);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveWorkerCountsAreRejected(int workers)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CreateSettings(maxConcurrentWorkers: workers));
    }

    [Fact]
    public void GenerationContinuationBudgetsDefaultToAtLeastSixteen()
    {
        var settings = CreateSettings(maxConcurrentWorkers: 1);

        Assert.Equal(16, settings.MaxGenerationCompletionsPerFrame);
        Assert.Equal(16, settings.MaxGenerationTaskAdvancesPerFrame);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(-1, 1)]
    [InlineData(1, 0)]
    [InlineData(1, -1)]
    public void NonPositiveGenerationContinuationBudgetsAreRejected(
        int completions,
        int advances)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSettings(
            maxGenerationCompletionsPerFrame: completions,
            maxGenerationTaskAdvancesPerFrame: advances));
    }

    private static ChunkResidencySettings CreateSettings(
        int maxConcurrentWorkers = 2,
        int maxResultsPerFrame = 8,
        int? maxGenerationCompletionsPerFrame = null,
        int? maxGenerationTaskAdvancesPerFrame = null) =>
        new(
            maxConcurrentWorkers,
            maxResultsPerFrame,
            maxEvictionsPerFrame: 2,
            new WorldGameRules(),
            maxGenerationCompletionsPerFrame,
            maxGenerationTaskAdvancesPerFrame);
}
