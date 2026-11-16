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
    private sealed class WorkerSlot(WorldWorkerPool workerPool)
    {
        public FluidMeshWorker Worker { get; } =
            new(workerPool ?? throw new ArgumentNullException(nameof(workerPool)));

        public WorldMeshBatch? InFlightBatch { get; set; }

        public WorldWorkerPriority WorkerPriority { get; set; } =
            WorldWorkerPriority.FromPriorityClass(
                WorldWorkerPool.BackgroundWorldPriority);
    }

    private readonly int _maximumMeshletsPerWorker;
    private readonly WorldWorkerPool _workerPool;
    private readonly List<WorkerSlot> _workers = [];

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
        WorldWorkerPool workerPool)
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

        _maximumMeshletsPerWorker =
            maximumMeshletsPerWorker;
        _workerPool = workerPool ??
            throw new ArgumentNullException(nameof(workerPool));
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

    public bool HasAvailableWorkerSlot => _acceptingWork;

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

        UpdateInFlightWorkerPriorities(center, movement);

        if (!_updates.HasWork)
            return false;

        var nearest = _dispatchFairness.ShouldPrioritizeNearby(
            center is not null);
        var drained = _updates.Drain(
            _maximumMeshletsPerWorker,
            nearest ? center : null,
            movement,
            MeshWorkerSnapshotLimits.MaximumSourceChunksPerBatch);

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

        var worker = new WorkerSlot(_workerPool);
        var relightable = _presentations.CaptureRelightableFluid(batch);
        var workerPriority = ResolveWorkerPriority(
            batch,
            center,
            movement,
            WorldWorkerPriority.FromPriorityClass(
                nearest
                    ? WorldWorkerPool.InteractiveWorldPriority
                    : WorldWorkerPool.BackgroundWorldPriority));
        if (!worker.Worker.TryStart(
                _world,
                _blocks,
                _fluids,
                batch,
                _revisions,
                relightable,
                workerPriority))
        {
            _updates.Requeue(batch);
            return false;
        }

        worker.InFlightBatch = batch;
        worker.WorkerPriority = workerPriority;
        _workers.Add(worker);
        _dispatchFairness.RecordStarted(nearest);
        return true;
    }

    private void UpdateInFlightWorkerPriorities(
        ChunkCoord? center,
        ChunkMovementDirection movement)
    {
        if (center is null)
            return;

        foreach (var worker in _workers)
        {
            if (worker.InFlightBatch is not { } batch)
                continue;

            var priority = ResolveWorkerPriority(
                batch, center, movement, worker.WorkerPriority);
            if (priority != worker.WorkerPriority &&
                worker.Worker.TryUpdatePriority(priority))
                worker.WorkerPriority = priority;
        }
    }

    private static WorldWorkerPriority ResolveWorkerPriority(
        WorldMeshBatch batch,
        ChunkCoord? center,
        ChunkMovementDirection movement,
        WorldWorkerPriority fallbackPriority)
    {
        if (center is not { } focus || batch.DirtyMeshlets.Count == 0)
            return fallbackPriority;

        ChunkGenerationTaskPriority? nearest = null;
        foreach (var coord in batch.DirtyMeshlets.Keys)
        {
            var priority = ChunkStreamingState.StreamingPriority(
                coord, focus, movement);
            if (nearest is null || priority.CompareTo(nearest.Value) < 0)
                nearest = priority;
        }

        return nearest is { } ticket
            ? WorldWorkerPriority.FromGenerationTicket(ticket)
            : fallbackPriority;
    }

    public bool TryPollCompleted(
        out MeshPipelineWorkerReport? report,
        out Exception? error)
    {
        report = null;
        error = null;

        for (var workerIndex = 0; workerIndex < _workers.Count; workerIndex++)
        {
            var worker = _workers[workerIndex];
            if (!worker.Worker.TryTakeCompleted(
                    out var result,
                    out error))
            {
                continue;
            }

            var sourceBatch = worker.InFlightBatch;
            worker.InFlightBatch = null;
            _workers.RemoveAt(workerIndex);

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
