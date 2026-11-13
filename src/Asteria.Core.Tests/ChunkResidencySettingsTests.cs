using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class ChunkResidencySettingsTests
{
    [Fact]
    public void GenerationContinuationBudgetsAreIndependentFromLegacyFrameBudgets()
    {
        var settings = CreateSettings(
            maxMaterializationsInFlight: 2,
            maxDispatchesPerFrame: 2,
            maxResultsPerFrame: 2,
            maxConcurrentWorkers: 3);

        Assert.Equal(2, settings.MaxDispatchesPerFrame);
        Assert.Equal(2, settings.MaxResultsPerFrame);
        Assert.Equal(24, settings.MaxGenerationCompletionsPerFrame);
        Assert.Equal(24, settings.MaxGenerationTaskAdvancesPerFrame);
    }

    [Fact]
    public void GenerationContinuationBudgetsCanBeConfiguredIndependently()
    {
        var settings = CreateSettings(
            maxMaterializationsInFlight: 2,
            maxDispatchesPerFrame: 1,
            maxResultsPerFrame: 3,
            maxConcurrentWorkers: 4,
            maxGenerationCompletionsPerFrame: 19,
            maxGenerationTaskAdvancesPerFrame: 27);

        Assert.Equal(1, settings.MaxDispatchesPerFrame);
        Assert.Equal(3, settings.MaxResultsPerFrame);
        Assert.Equal(19, settings.MaxGenerationCompletionsPerFrame);
        Assert.Equal(27, settings.MaxGenerationTaskAdvancesPerFrame);
    }

    [Fact]
    public void TerrainAdmissionCanExceedThePhysicalWorkerPool()
    {
        var settings = CreateSettings(
            maxMaterializationsInFlight: 6,
            maxDispatchesPerFrame: 3,
            maxResultsPerFrame: 8,
            maxConcurrentWorkers: 3);

        Assert.Equal(6, settings.MaxTerrainMaterializationsInFlight);
        Assert.Equal(3, settings.MaxDispatchesPerFrame);
        Assert.Equal(3, settings.MaxConcurrentWorkers);
        Assert.Equal(3, settings.MaxMaterializationsInFlight);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositiveWorkerCountsAreRejected(int workers)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CreateSettings(
            maxMaterializationsInFlight: 1,
            maxDispatchesPerFrame: 1,
            maxResultsPerFrame: 1,
            maxConcurrentWorkers: workers));
    }

    [Fact]
    public void GenerationContinuationBudgetsDefaultToAtLeastSixteen()
    {
        var settings = CreateSettings(
            maxMaterializationsInFlight: 1,
            maxDispatchesPerFrame: 1,
            maxResultsPerFrame: 1,
            maxConcurrentWorkers: 1);

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
        int maxMaterializationsInFlight = 2,
        int maxDispatchesPerFrame = 2,
        int maxResultsPerFrame = 2,
        int maxConcurrentWorkers = 2,
        int? maxGenerationCompletionsPerFrame = null,
        int? maxGenerationTaskAdvancesPerFrame = null) =>
        new(
            maxMaterializationsInFlight,
            maxDispatchesPerFrame,
            maxResultsPerFrame,
            maxEvictionsPerFrame: 2,
            new WorldGameRules(),
            maxConcurrentWorkers,
            maxGenerationCompletionsPerFrame,
            maxGenerationTaskAdvancesPerFrame);
}
