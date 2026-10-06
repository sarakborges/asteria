using Asteria.Core.World;

namespace Asteria.Client.Rendering;

public sealed class ChunkStreamingControllerSettings
{
    public ChunkStreamingControllerSettings(
        int renderDistanceChunks,
        int retentionMarginChunks,
        int maximumPresentationReservationsPerFrame)
    {
        if (renderDistanceChunks <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(renderDistanceChunks));
        }

        if (retentionMarginChunks < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retentionMarginChunks));
        }

        if (maximumPresentationReservationsPerFrame <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumPresentationReservationsPerFrame));
        }

        RenderDistanceChunks =
            renderDistanceChunks;
        RetentionMarginChunks =
            retentionMarginChunks;
        MaximumPresentationReservationsPerFrame =
            maximumPresentationReservationsPerFrame;
    }

    public int RenderDistanceChunks { get; }

    public int RetentionMarginChunks { get; }

    public int MaximumPresentationReservationsPerFrame { get; }
}

public readonly record struct ChunkStreamingSelectionReport(
    bool Changed,
    ChunkCoord Center,
    int DesiredCount,
    int RetainedCount,
    int PendingCount,
    ChunkMovementDirection MovementDirection);

public sealed record ChunkStreamingBeginFrameReport(
    ChunkStreamingSelectionReport Selection,
    ChunkResidencyUpdate Collected,
    ChunkResidencyUpdate Dispatched,
    int ReservedPresentations);

public sealed record ChunkStreamingEndFrameReport(
    IReadOnlyList<ChunkResidencyRetirement> Retirements);

public sealed class ChunkStreamingController
{
    private readonly ChunkResidencyRuntime _residency;
    private readonly ChunkPresentationController _presentations;
    private readonly ChunkSurfaceRangeWindow _surfaceRanges;
    private readonly ChunkStreamingControllerSettings _settings;

    public ChunkStreamingController(
        ChunkResidencyRuntime residency,
        ChunkPresentationController presentations,
        IChunkSurfaceRangeProvider surfaceRanges,
        ChunkStreamingControllerSettings settings)
    {
        _residency =
            residency ??
            throw new ArgumentNullException(nameof(residency));
        _presentations =
            presentations ??
            throw new ArgumentNullException(nameof(presentations));
        _surfaceRanges =
            new ChunkSurfaceRangeWindow(
                surfaceRanges ??
                throw new ArgumentNullException(
                    nameof(surfaceRanges)));
        _settings =
            settings ??
            throw new ArgumentNullException(nameof(settings));
    }

    public ChunkStreamingSelectionReport SyncSelection(
        ChunkCoord center)
    {
        if (!_residency.SelectionNeedsRebuild(
                center,
                _settings.RenderDistanceChunks))
        {
            return new ChunkStreamingSelectionReport(
                false,
                center,
                _residency.DesiredCount,
                _residency.RetainedCount,
                _residency.PendingCount,
                _residency.MovementDirection);
        }

        _surfaceRanges.RetainWindow(
            center.X,
            center.Z,
            _settings.RenderDistanceChunks);

        var desired =
            ChunkStreamingSelection.DesiredSurfaceChunks(
                center,
                _settings.RenderDistanceChunks,
                _surfaceRanges);
        var retentionRadius =
            _settings.RenderDistanceChunks +
            _settings.RetentionMarginChunks;
        var changed =
            _residency.SyncSelection(
                center,
                _settings.RenderDistanceChunks,
                retentionRadius,
                desired,
                _presentations.Coordinates);

        return new ChunkStreamingSelectionReport(
            changed,
            center,
            _residency.DesiredCount,
            _residency.RetainedCount,
            _residency.PendingCount,
            _residency.MovementDirection);
    }

    public ChunkStreamingBeginFrameReport BeginFrame(
        ChunkCoord center,
        WorldFrameWorkBudget budget)
    {
        var selection =
            SyncSelection(center);
        var collected =
            _residency.CollectMaterializationResults(
                budget,
                _presentations.Coordinates);
        var dispatched =
            _residency.DispatchMaterializationTasks(
                budget,
                _presentations.Coordinates);
        var reserved =
            _presentations.ReservePending(
                _residency,
                _settings.MaximumPresentationReservationsPerFrame,
                budget);

        if (reserved > 0)
        {
            _residency.SyncResidentState(
                _presentations.Coordinates);
        }

        return new ChunkStreamingBeginFrameReport(
            selection,
            collected,
            dispatched,
            reserved);
    }

    public ChunkStreamingEndFrameReport EndFrame(
        WorldFrameWorkBudget budget)
    {
        _presentations.SyncVisibility(
            _residency);

        var retirements =
            _residency.RetireDistantChunks(
                budget);

        foreach (var retirement in retirements)
        {
            _presentations.Retire(
                retirement.Coord);
        }

        return new ChunkStreamingEndFrameReport(
            retirements);
    }
}
