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
    private readonly ChunkStreamingSelectionRuntime _selection;
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
        _selection =
            new ChunkStreamingSelectionRuntime(
                surfaceRanges ??
                throw new ArgumentNullException(
                    nameof(surfaceRanges)));
        _settings =
            settings ??
            throw new ArgumentNullException(nameof(settings));
    }

    public bool IsSelectionRunning =>
        _selection.IsRunning;

    public void BeginRetirement() =>
        _selection.BeginRetirement();

    public bool TryDrainSelection(
        out Exception? error) =>
        _selection.TryPollCompleted(
            out _,
            out error);

    public ChunkStreamingSelectionReport SyncSelection(
        ChunkCoord center) =>
        SyncSelection(
            center,
            _settings.RenderDistanceChunks);

    public ChunkStreamingSelectionReport SyncSelection(
        ChunkCoord center,
        int horizontalRadius)
    {
        if (horizontalRadius <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horizontalRadius));
        }

        var changed = false;
        var radius =
            horizontalRadius;
        var retentionRadius =
            radius +
            _settings.RetentionMarginChunks;

        if (_residency.SelectionNeedsRebuild(
                center,
                radius))
        {
            var bootstrap =
                ChunkStreamingSelection
                    .DesiredPlayerLocalChunks(
                        center,
                        radius);
            changed =
                _residency.SyncSelection(
                    center,
                    radius,
                    retentionRadius,
                    bootstrap,
                    _presentations.Coordinates);
            _selection.Request(
                center,
                radius);
        }

        if (_selection.TryPollCompleted(
                out var completed,
                out var error))
        {
            if (error is not null)
            {
                throw new InvalidOperationException(
                    "Chunk streaming selection worker failed.",
                    error);
            }

            if (completed is not null)
            {
                changed =
                    _residency.SyncSelection(
                        completed.Center,
                        completed.HorizontalRadius,
                        completed.HorizontalRadius +
                        _settings.RetentionMarginChunks,
                        completed.Desired,
                        _presentations.Coordinates) ||
                    changed;
            }
        }

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
        WorldFrameWorkBudget budget) =>
        BeginFrame(
            center,
            budget,
            _settings.RenderDistanceChunks);

    public ChunkStreamingBeginFrameReport BeginFrame(
        ChunkCoord center,
        WorldFrameWorkBudget budget,
        int horizontalRadius)
    {
        var selection =
            SyncSelection(
                center,
                horizontalRadius);
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
