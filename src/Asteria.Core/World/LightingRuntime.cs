using System.Diagnostics;
using Asteria.Core.Content;

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
    double ApplyMilliseconds,
    int SeedCount = 0,
    LightingDependencyChanges StaleDependencies = LightingDependencyChanges.None,
    int MediumSampleRequests = 0,
    int MediumSampleCacheMisses = 0);

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
    private readonly PackContentRegistry<ObjectDefinition>? _objects;
    private readonly WorldUpdateQueue _updates;
    private readonly LightingResultIntegrator _integration;
    private readonly LightingWorker _worker = new();

    private bool _acceptingWork = true;

    private WorldLightingBatch? _inFlightBatch;
    private readonly ProximityDispatchFairness _dispatchFairness = new();
    private double _inFlightCaptureMilliseconds;

    // Worker-prepared spatial masks avoid per-voxel halo enumeration on
    // the authoritative thread. Bound both chunks and changed voxels.
    private const int MaximumPublishedChunksPerPoll = 16;
    private const int MaximumPublishedVoxelsPerPoll = 8192;
    // Coalesce up to eight polls of a lighting result before scheduling
    // presentation rebuilds. This bounds visual lag to about eight frames
    // while preventing repeated remesh of the same meshlet each slice.
    private const int MaximumVoxelsPerMeshRefresh = 8192;
    private LightingWorkerResult? _publication;
    private double _publicationCaptureMilliseconds;
    private double _publicationApplyMilliseconds;
    private int _publicationChunkOffset;
    private int _publicationDirtyChunks;
    private int _publicationDirtyMeshlets;
    private int _publicationVoxelsSinceRefresh;

    public LightingRuntime(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        WorldUpdateQueue updates,
        LightingResultIntegrator integration,
        PackContentRegistry<ObjectDefinition>? objects = null)
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
        _objects = objects;
    }

    public bool IsRunning => _worker.IsRunning || _publication is not null;

    public void BeginRetirement()
    {
        _acceptingWork = false;
    }

    public bool TryStartReadyWork(
        ChunkCoord? center = null,
        ChunkMovementDirection movement = default)
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

        var nearest = _dispatchFairness.ShouldPrioritizeNearby(
            center is not null);
        var batch = _updates.DrainLighting(
            MaximumSeedsPerWorker,
            MaximumSeedColumnsPerWorker,
            _world,
            nearest ? center : null,
            movement);

        if (batch.IsEmpty)
        {
            return false;
        }

        var captureStart = Stopwatch.GetTimestamp();
        if (!_worker.TryStart(
                _world,
                _blocks,
                _fluids,
                batch, _objects))
        {
            _updates.RequeueLighting(batch, _world);
            return false;
        }

        _inFlightCaptureMilliseconds =
            Stopwatch.GetElapsedTime(captureStart).TotalMilliseconds;
        _inFlightBatch = batch;
        _dispatchFairness.RecordStarted(nearest);
        return true;
    }

    public bool TryPollCompleted(
        out LightingRuntimeReport? report,
        out Exception? error)
    {
        report = null;
        error = null;

        if (_publication is not null)
            return TryPublishNextSlice(out report);

        // Reject obsolete work before it finishes computing; cancellation
        // is checked inside the snapshot-only solver, never against live
        // mutable chunks. Completion still owns the original batch.
        _worker.CancelIfStale(_world);
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
                    sourceBatch, _world);
            }
            return true;
        }

        if (result is null)
        {
            if (sourceBatch is not null)
            {
                _updates.RequeueLighting(
                    sourceBatch, _world);
            }
            return true;
        }

        if (result.Cancelled || !result.Dependencies.IsCurrent(
                _world))
        {
            _updates.RequeueLighting(
                result.SourceBatch, _world);
            report =
                new LightingRuntimeReport(
                    LightingCompletionKind.RequeuedStale,
                    result.WorkerMilliseconds,
                    0,
                    0,
                    0,
                    result.Lighting.ProcessedVoxelCount,
                    captureMilliseconds,
                    0d,
                    result.SourceBatch.EditedPositions.Count,
                    result.Dependencies.DetectChanges(_world),
                    result.Lighting.MediumSampleRequests,
                    result.Lighting.MediumSampleCacheMisses);
            return true;
        }

        _publication = result;
        _publicationCaptureMilliseconds = captureMilliseconds;
        _publicationApplyMilliseconds = 0d;
        _publicationChunkOffset = 0;
        _publicationDirtyChunks = 0;
        _publicationDirtyMeshlets = 0;
        _publicationVoxelsSinceRefresh = 0;
        return TryPublishNextSlice(out report);
    }
    private bool TryPublishNextSlice(out LightingRuntimeReport? report)
    {
        report = null;
        var result = _publication ??
            throw new InvalidOperationException("No lighting publication pending.");

        // Chunk content or residency can change between frames. Do not
        // publish obsolete light; requeue its original seeds for reconciliation.
        if (!result.Dependencies.IsCurrent(_world))
        {
            // Earlier slices already mutated authoritative lighting. Flush
            // their mesh refreshes even when later dependencies turn stale.
            FlushLightingRefreshes();
            _updates.RequeueLighting(result.SourceBatch, _world);
            report = new LightingRuntimeReport(
                LightingCompletionKind.RequeuedStale,
                result.WorkerMilliseconds, 0, 0, 0,
                result.Lighting.ProcessedVoxelCount,
                _publicationCaptureMilliseconds,
                _publicationApplyMilliseconds,
                result.SourceBatch.EditedPositions.Count,
                result.Dependencies.DetectChanges(_world),
                result.Lighting.MediumSampleRequests,
                result.Lighting.MediumSampleCacheMisses);
            _publication = null;
            return true;
        }

        var publications = result.PublicationChunks ??
            throw new InvalidOperationException(
                "Completed lighting worker has no publication plan.");
        var chunks = 0;
        var voxels = 0;
        while (_publicationChunkOffset < publications.Count &&
               chunks < MaximumPublishedChunksPerPoll)
        {
            var next = publications[_publicationChunkOffset];
            if (chunks > 0 &&
                voxels + next.ChangedVoxelCount > MaximumPublishedVoxelsPerPoll)
                break;

            var started = Stopwatch.GetTimestamp();
            _integration.AccumulatePreparedChunk(result.LightingSnapshot, next);
            _publicationApplyMilliseconds +=
                Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            _publicationVoxelsSinceRefresh += next.ChangedVoxelCount;
            voxels += next.ChangedVoxelCount;
            _publicationChunkOffset++;
            chunks++;
            if (_publicationVoxelsSinceRefresh >= MaximumVoxelsPerMeshRefresh)
                FlushLightingRefreshes();
        }

        if (_publicationChunkOffset < publications.Count)
            return false;

        FlushLightingRefreshes();
        report = new LightingRuntimeReport(
            LightingCompletionKind.Applied,
            result.WorkerMilliseconds,
            result.Lighting.ChangedPositions.Count,
            _publicationDirtyChunks,
            _publicationDirtyMeshlets,
            result.Lighting.ProcessedVoxelCount,
            _publicationCaptureMilliseconds,
            _publicationApplyMilliseconds,
            result.SourceBatch.EditedPositions.Count,
            MediumSampleRequests: result.Lighting.MediumSampleRequests,
            MediumSampleCacheMisses: result.Lighting.MediumSampleCacheMisses);
        _publication = null;
        return true;
    }

    private void FlushLightingRefreshes()
    {
        if (_publicationVoxelsSinceRefresh == 0)
            return;

        var started = Stopwatch.GetTimestamp();
        var refreshed = _integration.FlushAccumulated();
        _publicationApplyMilliseconds +=
            Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        _publicationDirtyChunks += refreshed.DirtyChunkCount;
        _publicationDirtyMeshlets += refreshed.DirtyMeshletCount;
        _publicationVoxelsSinceRefresh = 0;
    }
}
