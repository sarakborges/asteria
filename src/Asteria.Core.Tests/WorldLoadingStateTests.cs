using Asteria.Core.World;

namespace Asteria.Core.Tests;

public sealed class WorldLoadingStateTests
{
    [Fact]
    public void CurrentSelectionAndSettledPresentationAreRequiredBeforeReady()
    {
        var loading = new WorldLoadingState();
        loading.Begin(new ChunkCoord(5, 3, -7));

        loading.UpdateResidency(25, 0, 0, selectionRunning: true);
        Assert.Equal(WorldLoadingPhase.MaterializingInitialArea, loading.Progress.Phase);

        // The active startup selection remains authoritative until its
        // surface-range result is known and all of its real work is settled.
        loading.UpdateResidency(264, 4, 2, selectionRunning: false);
        Assert.Equal(258, loading.Progress.Completed);
        Assert.Equal(264, loading.Progress.Total);
        loading.UpdateResidency(264, 0, 0, selectionRunning: false);
        Assert.Equal(WorldLoadingPhase.PreparingPresentation, loading.Progress.Phase);

        Assert.False(loading.UpdatePresentation(25, 264, SettledReadiness()));
        Assert.Equal(WorldLoadingPhase.PreparingPresentation, loading.Progress.Phase);
        Assert.Equal(25, loading.Progress.Completed);
        Assert.Equal(264, loading.Progress.Total);

        Assert.False(loading.UpdatePresentation(264, 264, SettledReadiness() with { LightingSettled = false }));
        Assert.False(loading.Progress.IsReady);
        Assert.True(loading.UpdatePresentation(264, 264, SettledReadiness()));
        Assert.True(loading.Progress.IsReady);
    }

    [Fact]
    public void SeamLightingDispatchWaitsUntilInitialResidencySettles()
    {
        var loading = new WorldLoadingState();
        Assert.True(loading.AllowsLightingDispatch);

        loading.Begin(ChunkCoord.Zero);
        Assert.False(loading.AllowsLightingDispatch);

        loading.UpdateResidency(
            desired: 4, pending: 2, materializing: 0, selectionRunning: false);
        Assert.False(loading.AllowsLightingDispatch);

        loading.UpdateResidency(
            desired: 4, pending: 0, materializing: 0, selectionRunning: false);
        Assert.Equal(
            WorldLoadingPhase.PreparingPresentation,
            loading.Progress.Phase);
        Assert.True(loading.AllowsLightingDispatch);

        loading.BeginRetirement();
        Assert.False(loading.AllowsLightingDispatch);

        loading.Reset();
        Assert.True(loading.AllowsLightingDispatch);
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
        Assert.True(loading.UpdatePresentation(1, 1, SettledReadiness()));
        loading.BeginArrivalStructurePresentation();

        loading.UpdateArrivalStructurePresentation(presentationCurrent: false);
        Assert.Equal(WorldLoadingPhase.PreparingArrivalStructure, loading.Progress.Phase);
        loading.UpdateArrivalStructurePresentation(presentationCurrent: true);
        Assert.True(loading.Progress.IsReady);
    }

    [Fact]
    public void EveryBootstrapPresentationDependencyMustBeSettled()
    {
        var readiness = SettledReadiness();

        Assert.True(readiness.IsSettled);
        Assert.False((readiness with { ResidencySettled = false }).IsSettled);
        Assert.False((readiness with { SelectionSettled = false }).IsSettled);
        Assert.False((readiness with { LightingSettled = false }).IsSettled);
        Assert.False((readiness with { TerrainMeshWorkSettled = false }).IsSettled);
        Assert.False((readiness with { TerrainPublicationsSettled = false }).IsSettled);
        Assert.False((readiness with { FluidMeshWorkSettled = false }).IsSettled);
        Assert.False((readiness with { FluidPublicationsSettled = false }).IsSettled);
        Assert.False((readiness with { ObjectPresentationSettled = false }).IsSettled);
    }

    [Fact]
    public void InvalidPresentationCountsAreRejected()
    {
        var loading = new WorldLoadingState();
        loading.Begin(ChunkCoord.Zero);
        loading.UpdateResidency(1, 0, 0, selectionRunning: false);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            loading.UpdatePresentation(2, 1, SettledReadiness()));
    }

    private static WorldLoadingReadiness SettledReadiness() =>
        new(
            ResidencySettled: true,
            SelectionSettled: true,
            LightingSettled: true,
            TerrainMeshWorkSettled: true,
            TerrainPublicationsSettled: true,
            FluidMeshWorkSettled: true,
            FluidPublicationsSettled: true,
            ObjectPresentationSettled: true);
}
