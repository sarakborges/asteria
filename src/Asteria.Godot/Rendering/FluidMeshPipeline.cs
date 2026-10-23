using Asteria.Core.World;

namespace Asteria.Client.Rendering;

public sealed class FluidMeshPipeline
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly FluidRegistry _fluids;
    private readonly FluidMeshUpdateQueue _updates;
    private readonly MeshletContentRevisions _revisions;
    private readonly ChunkPresentationController _presentations;
    private sealed class WorkerSlot
    {
        public FluidMeshWorker Worker { get; } = new();

        public WorldMeshBatch? InFlightBatch { get; set; }
    }

    private readonly int _maximumMeshletsPerWorker;
    private readonly WorkerSlot[] _workers;

    private bool _acceptingWork = true;

    private readonly ProximityDispatchFairness _dispatchFairness = new();

    public FluidMeshPipeline(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        FluidMeshUpdateQueue updates,
        MeshletContentRevisions revisions,
        ChunkPresentationController presentations,
        int maximumMeshletsPerWorker,
        int maximumConcurrentWorkers)
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
        _revisions =
            revisions ??
            throw new ArgumentNullException(nameof(revisions));
        _presentations =
            presentations ??
            throw new ArgumentNullException(nameof(presentations));

        if (maximumMeshletsPerWorker <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumMeshletsPerWorker));
        }

        if (maximumConcurrentWorkers <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumConcurrentWorkers));
        }

        _maximumMeshletsPerWorker =
            maximumMeshletsPerWorker;
        _workers = new WorkerSlot[maximumConcurrentWorkers];
        for (var index = 0; index < _workers.Length; index++)
            _workers[index] = new WorkerSlot();
    }

    public bool IsRunning => ActiveWorkerCount > 0;

    public int ActiveWorkerCount
    {
        get
        {
            var count = 0;
            foreach (var worker in _workers)
                if (worker.Worker.IsRunning)
                    count++;
            return count;
        }
    }

    public bool HasWorkAffecting(Func<ChunkCoord, bool> isRelevantChunk)
    {
        ArgumentNullException.ThrowIfNull(isRelevantChunk);
        if (_updates.HasWorkInChunks(isRelevantChunk))
            return true;

        foreach (var worker in _workers)
        {
            if (worker.InFlightBatch is { } batch &&
                batch.DirtyMeshlets.Keys.Any(isRelevantChunk))
                return true;
            if (worker.Worker.IsRunning && worker.InFlightBatch is null)
                return true;
        }
        return false;
    }

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

        if (!_updates.HasWork)
            return false;

        WorkerSlot? availableWorker = null;
        foreach (var worker in _workers)
        {
            if (worker.Worker.IsRunning)
                continue;
            availableWorker = worker;
            break;
        }

        if (availableWorker is null)
            return false;

        var nearest = _dispatchFairness.ShouldPrioritizeNearby(
            center is not null);
        var drained = _updates.Drain(
            _maximumMeshletsPerWorker,
            nearest ? center : null,
            movement);

        if (drained.IsEmpty)
        {
            return false;
        }

        var batch =
            MeshPipelineWorkSelection.FilterPresented(
                _world,
                _presentations,
                drained);

        if (batch.IsEmpty)
        {
            return false;
        }

        var relightable = _presentations.CaptureRelightableFluid(batch);
        if (!availableWorker.Worker.TryStart(
                _world,
                _blocks,
                _fluids,
                batch,
                _revisions,
                relightable))
        {
            _updates.Requeue(batch);
            return false;
        }

        availableWorker.InFlightBatch = batch;
        _dispatchFairness.RecordStarted(nearest);
        return true;
    }

    public bool TryPollCompleted(
        out MeshPipelineWorkerReport? report,
        out Exception? error)
    {
        report = null;
        error = null;

        foreach (var worker in _workers)
        {
            if (!worker.Worker.TryTakeCompleted(
                    out var result,
                    out error))
            {
                continue;
            }

            var sourceBatch = worker.InFlightBatch;
            worker.InFlightBatch = null;

            if (error is not null)
            {
                if (sourceBatch is not null)
                    _updates.Requeue(sourceBatch);
                return true;
            }

            if (result is null)
            {
                if (sourceBatch is not null)
                    _updates.Requeue(sourceBatch);
                return true;
            }

            var batchStale = !result.Dependencies.IsCurrent(_world);
            var accepted = 0;
            var stale = 0;
            var salvaged = 0;
            var relitWithoutMeshing = 0;

            foreach (var meshlet in result.Meshlets)
            {
                if (!_world.ContainsChunk(meshlet.Coord) ||
                    !_presentations.Contains(meshlet.Coord))
                {
                    continue;
                }

                var key = new ChunkMeshletKey(
                    meshlet.Coord,
                    meshlet.MeshletIndex);
                var revision = result.ContentRevisions[key];
                var dependencies = result.MeshletDependencies[key];

                if (_revisions.IsCurrent(key, revision) &&
                    dependencies.IsCurrent(_world))
                {
                    _presentations.EnqueueFluidPublication(
                        meshlet,
                        revision,
                        dependencies);
                    accepted++;
                    if (batchStale)
                        salvaged++;
                    if (meshlet.LightOnly)
                        relitWithoutMeshing++;
                }
                else
                {
                    _updates.EnqueueMeshlets(
                        meshlet.Coord,
                        ChunkMeshletMask.Single(meshlet.MeshletIndex));
                    stale++;
                }
            }

            report = new MeshPipelineWorkerReport(
                accepted == 0 && stale > 0
                    ? MeshPipelineCompletionKind.RequeuedStale
                    : MeshPipelineCompletionKind.Applied,
                result.WorkerMilliseconds,
                accepted,
                stale,
                CaptureMilliseconds: result.CaptureMilliseconds,
                RelitWithoutMeshing: relitWithoutMeshing,
                SalvagedFromStaleBatch: salvaged);
            return true;
        }

        return false;
    }

    public ChunkPresentationIntegrationStats IntegratePublications(
        int maximumPerFrame,
        WorldFrameWorkBudget budget) =>
        _presentations.IntegrateFluidPublications(
            maximumPerFrame,
            budget);
}
