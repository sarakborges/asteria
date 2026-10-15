using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldLoadingStateTests
{
    [Fact]
    public void FullSelectionAndSettledPresentationAreRequiredBeforeReady()
    {
        var loading = new WorldLoadingState();
        loading.Begin(new ChunkCoord(5, 3, -7));

        loading.UpdateResidency(25, 0, 0, selectionRunning: true);
        Assert.Equal(WorldLoadingPhase.MaterializingInitialArea, loading.Progress.Phase);

        // The streaming selection is not stable until its full surface
        // range is known; a small spawn bootstrap must not release gameplay.
        loading.UpdateResidency(264, 4, 2, selectionRunning: false);
        Assert.Equal(258, loading.Progress.Completed);
        Assert.Equal(264, loading.Progress.Total);
        loading.UpdateResidency(264, 0, 0, selectionRunning: false);
        Assert.Equal(WorldLoadingPhase.PreparingPresentation, loading.Progress.Phase);

        Assert.False(loading.UpdatePresentation(25, 264, workSettled: true));
        Assert.Equal(WorldLoadingPhase.PreparingPresentation, loading.Progress.Phase);
        Assert.Equal(25, loading.Progress.Completed);
        Assert.Equal(264, loading.Progress.Total);

        Assert.False(loading.UpdatePresentation(264, 264, workSettled: false));
        Assert.False(loading.Progress.IsReady);
        Assert.True(loading.UpdatePresentation(264, 264, workSettled: true));
        Assert.True(loading.Progress.IsReady);
    }

    [Fact]
    public void ZeroDesiredSelectionCannotBecomeReady()
    {
        var loading = new WorldLoadingState();
        loading.Begin(ChunkCoord.Zero);
        loading.UpdateResidency(0, 0, 0, selectionRunning: false);
        Assert.Equal(WorldLoadingPhase.MaterializingInitialArea, loading.Progress.Phase);
    }

    [Fact]
    public void PortalArrivalCanReturnToReadyOnlyAfterPresentationIsCurrent()
    {
        var loading = new WorldLoadingState();
        loading.Begin(ChunkCoord.Zero);
        loading.UpdateResidency(1, 0, 0, selectionRunning: false);
        Assert.True(loading.UpdatePresentation(1, 1, workSettled: true));
        loading.BeginArrivalStructurePresentation();

        loading.UpdateArrivalStructurePresentation(presentationCurrent: false);
        Assert.Equal(WorldLoadingPhase.PreparingArrivalStructure, loading.Progress.Phase);
        loading.UpdateArrivalStructurePresentation(presentationCurrent: true);
        Assert.True(loading.Progress.IsReady);
    }

    [Fact]
    public void InvalidPresentationCountsAreRejected()
    {
        var loading = new WorldLoadingState();
        loading.Begin(ChunkCoord.Zero);
        loading.UpdateResidency(1, 0, 0, selectionRunning: false);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            loading.UpdatePresentation(2, 1, workSettled: true));
    }
}
