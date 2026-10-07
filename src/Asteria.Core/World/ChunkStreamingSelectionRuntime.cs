namespace Asteria.Core.World;

public sealed record ChunkStreamingSelectionSnapshot(
    ulong Revision,
    ChunkCoord Center,
    int HorizontalRadius,
    IReadOnlySet<ChunkCoord> Desired);

/// <summary>
/// Owns asynchronous full surface-range selection. A cheap player-local
/// selection can be published immediately while this worker resolves the
/// complete authored vertical range without blocking the caller.
/// </summary>
public sealed class ChunkStreamingSelectionRuntime
{
    private readonly ChunkSurfaceRangeWindow _surfaceRanges;
    private readonly SingleFlightWorker<
        ChunkStreamingSelectionSnapshot> _worker =
        new();

    private bool _acceptingWork = true;
    private ulong _revision;
    private SelectionRequest? _pending;
    private SelectionRequest? _inFlight;

    public ChunkStreamingSelectionRuntime(
        IChunkSurfaceRangeProvider surfaceRanges)
    {
        _surfaceRanges =
            new ChunkSurfaceRangeWindow(
                surfaceRanges ??
                throw new ArgumentNullException(
                    nameof(surfaceRanges)));
    }

    public bool IsRunning =>
        _worker.IsRunning;

    public void Request(
        ChunkCoord center,
        int horizontalRadius)
    {
        if (!_acceptingWork)
        {
            return;
        }

        if (horizontalRadius <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(horizontalRadius));
        }

        if (Matches(
                _pending,
                center,
                horizontalRadius) ||
            Matches(
                _inFlight,
                center,
                horizontalRadius))
        {
            return;
        }

        _revision++;
        _pending =
            new SelectionRequest(
                _revision,
                center,
                horizontalRadius);
        TryStartPending();
    }

    public void BeginRetirement()
    {
        _acceptingWork = false;
        _pending = null;
    }

    public bool TryPollCompleted(
        out ChunkStreamingSelectionSnapshot? result,
        out Exception? error)
    {
        result = null;
        error = null;

        if (!_worker.TryTakeCompleted(
                out var completed,
                out error))
        {
            return false;
        }

        var request =
            _inFlight;
        _inFlight = null;

        if (error is null &&
            completed is null)
        {
            error =
                new InvalidOperationException(
                    "Chunk streaming selection worker completed without a result.");
        }

        if (error is null &&
            completed is not null &&
            request is { } source &&
            completed.Revision ==
                source.Revision &&
            completed.Revision ==
                _revision)
        {
            result =
                completed;
        }

        TryStartPending();
        return true;
    }

    private void TryStartPending()
    {
        if (!_acceptingWork ||
            _worker.IsRunning ||
            _pending is not
                { } request)
        {
            return;
        }

        _pending = null;
        _inFlight =
            request;

        if (!_worker.TryStart(
                () =>
                    BuildSelection(
                        request)))
        {
            _inFlight = null;
            _pending =
                request;
        }
    }

    private ChunkStreamingSelectionSnapshot BuildSelection(
        SelectionRequest request)
    {
        _surfaceRanges.RetainWindow(
            request.Center.X,
            request.Center.Z,
            request.HorizontalRadius);

        var desired =
            ChunkStreamingSelection
                .DesiredSurfaceChunks(
                    request.Center,
                    request.HorizontalRadius,
                    _surfaceRanges);

        return new ChunkStreamingSelectionSnapshot(
            request.Revision,
            request.Center,
            request.HorizontalRadius,
            desired);
    }

    private static bool Matches(
        SelectionRequest? request,
        ChunkCoord center,
        int horizontalRadius) =>
        request is
            { } value &&
        value.Center ==
            center &&
        value.HorizontalRadius ==
            horizontalRadius;

    private readonly record struct SelectionRequest(
        ulong Revision,
        ChunkCoord Center,
        int HorizontalRadius);
}
