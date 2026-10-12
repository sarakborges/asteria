using System.Diagnostics;

namespace Asteria.Core.World;

public enum LightingCompletionKind : byte
{
    Applied = 0,
    RequeuedStale = 1,
}

public sealed record LightingRuntimeReport(
    LightingCompletionKind Kind,
    double WorkerMilliseconds,
    int ChangedVoxelCount,
    int DirtyChunkCount,
    int DirtyMeshletCount,
    int ProcessedVoxelCount,
    double CaptureMilliseconds,
    double ApplyMilliseconds);

public sealed class LightingRuntime
{
    // Snapshot capture is synchronous because it must observe one coherent
    // live-world state before the worker starts. Keep each capture bounded so
    // streaming cannot turn a large reconciliation backlog into a main-thread
    // clone spike.
    private const int MaximumSeedsPerWorker = 512;
    // Snapshot capture clones three-by-three neighboring chunk columns
    // synchronously. At most two seed columns keeps that managed-thread
    // operation bounded while allowing the full seed limit when nearby.
    private const int MaximumSeedColumnsPerWorker = 2;

    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly WorldUpdateQueue _updates;
    private readonly LightingResultIntegrator _integration;
    private readonly LightingWorker _worker = new();

    private bool _acceptingWork = true;

    private WorldLightingBatch? _inFlightBatch;
    private double _inFlightCaptureMilliseconds;

    // Keep large lighting updates from publishing tens of thousands of
    // voxels and meshlet invalidations in one main-thread frame.
    private const int MaximumPublishedVoxelsPerPoll = 1024;
    private LightingWorkerResult? _publication;
    private double _publicationCaptureMilliseconds;
    private double _publicationApplyMilliseconds;
    private int _publicationOffset;
    private int _publicationDirtyChunks;
    private int _publicationDirtyMeshlets;

    public LightingRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        WorldUpdateQueue updates,
        LightingResultIntegrator integration)
    {
        _world =
            world ??
            throw new ArgumentNullException(nameof(world));
        _blocks =
            blocks ??
            throw new ArgumentNullException(nameof(blocks));
        _fluids =
            fluids ??
            throw new ArgumentNullException(nameof(fluids));
        _updates =
            updates ??
            throw new ArgumentNullException(nameof(updates));
        _integration =
            integration ??
            throw new ArgumentNullException(nameof(integration));
    }

    public bool IsRunning => _worker.IsRunning;

    public void BeginRetirement()
    {
        _acceptingWork = false;
    }

    public bool TryStartReadyWork()
    {
        if (!_acceptingWork)
        {
            return false;
        }

        if (_worker.IsRunning ||
            _publication is not null ||
            !_updates.HasLightingWork)
        {
            return false;
        }

        var batch =
            _updates.DrainLighting(
                MaximumSeedsPerWorker,
                MaximumSeedColumnsPerWorker);

        if (batch.IsEmpty)
        {
            return false;
        }

        var captureStart = Stopwatch.GetTimestamp();
        if (!_worker.TryStart(
                _world,
                _blocks,
                _fluids,
                batch))
        {
            _updates.RequeueLighting(batch);
            return false;
        }

        _inFlightCaptureMilliseconds =
            Stopwatch.GetElapsedTime(captureStart).TotalMilliseconds;
        _inFlightBatch = batch;
        return true;
    }

    public bool TryPollCompleted(
        out LightingRuntimeReport? report,
        out Exception? error)
    {
        report = null;
        error = null;

        if (!_worker.TryTakeCompleted(
                out var result,
                out error))
        {
            return false;
        }

        var sourceBatch = _inFlightBatch;
        var captureMilliseconds = _inFlightCaptureMilliseconds;
        _inFlightBatch = null;
        _inFlightCaptureMilliseconds = 0d;

        if (error is not null)
        {
            if (sourceBatch is not null)
            {
                _updates.RequeueLighting(
                    sourceBatch);
            }
            return true;
        }

        if (result is null)
        {
            if (sourceBatch is not null)
            {
                _updates.RequeueLighting(
                    sourceBatch);
            }
            return true;
        }

        if (!result.Dependencies.IsCurrent(
                _world))
        {
            _updates.RequeueLighting(
                result.SourceBatch);
            report =
                new LightingRuntimeReport(
                    LightingCompletionKind.RequeuedStale,
                    result.WorkerMilliseconds,
                    0,
                    0,
                    0,
                    result.Lighting.ProcessedVoxelCount,
                    captureMilliseconds,
                    0d);
            return true;
        }

        var applyStart = Stopwatch.GetTimestamp();
        var integrated =
            _integration.Apply(
                result.LightingSnapshot,
                result.Lighting.ChangedPositions);
        var applyMilliseconds =
            Stopwatch.GetElapsedTime(applyStart).TotalMilliseconds;

        report =
            new LightingRuntimeReport(
                LightingCompletionKind.Applied,
                result.WorkerMilliseconds,
                integrated.ChangedVoxelCount,
                integrated.DirtyChunkCount,
                integrated.DirtyMeshletCount,
                result.Lighting.ProcessedVoxelCount,
                captureMilliseconds,
                applyMilliseconds);
        return true;
    }
}
