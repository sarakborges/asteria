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
    private readonly int _maximumMeshletsPerWorker;
    private readonly FluidMeshWorker _worker = new();

    private WorldMeshBatch? _inFlightBatch;

    public FluidMeshPipeline(
        VoxelWorld world,
        BlockRegistry blocks,
        FluidRegistry fluids,
        FluidMeshUpdateQueue updates,
        MeshletContentRevisions revisions,
        ChunkPresentationController presentations,
        int maximumMeshletsPerWorker)
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
    }

    public bool IsRunning => _worker.IsRunning;

    public bool TryStartReadyWork()
    {
        if (_worker.IsRunning ||
            !_updates.HasWork)
        {
            return false;
        }

        var drained =
            _updates.Drain(
                _maximumMeshletsPerWorker);

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

        if (!_worker.TryStart(
                _world,
                _blocks,
                _fluids,
                batch,
                _revisions))
        {
            _updates.Requeue(batch);
            return false;
        }

        _inFlightBatch = batch;
        return true;
    }

    public bool TryPollCompleted(
        out MeshPipelineWorkerReport? report,
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

        var sourceBatch =
            _inFlightBatch;
        _inFlightBatch = null;

        if (error is not null)
        {
            if (sourceBatch is not null)
            {
                _updates.Requeue(
                    sourceBatch);
            }

            TryStartReadyWork();
            return true;
        }

        if (result is null)
        {
            if (sourceBatch is not null)
            {
                _updates.Requeue(
                    sourceBatch);
            }

            TryStartReadyWork();
            return true;
        }

        if (!result.Dependencies.IsCurrent(
                _world))
        {
            _updates.Requeue(
                result.SourceBatch);
            report =
                new MeshPipelineWorkerReport(
                    MeshPipelineCompletionKind.RequeuedStale,
                    result.WorkerMilliseconds,
                    0,
                    MeshPipelineWorkSelection.CountMeshlets(
                        result.SourceBatch));
            TryStartReadyWork();
            return true;
        }

        var accepted = 0;
        var stale = 0;

        foreach (var meshlet in result.Meshlets)
        {
            if (!_world.ContainsChunk(
                    meshlet.Coord) ||
                !_presentations.Contains(
                    meshlet.Coord))
            {
                continue;
            }

            var key =
                new ChunkMeshletKey(
                    meshlet.Coord,
                    meshlet.MeshletIndex);
            var revision =
                result.ContentRevisions[key];

            if (_revisions.IsCurrent(
                    key,
                    revision))
            {
                _presentations.EnqueueFluidPublication(
                    meshlet,
                    revision,
                    result.Dependencies);
                accepted++;
            }
            else
            {
                _updates.EnqueueMeshlets(
                    meshlet.Coord,
                    ChunkMeshletMask.Single(
                        meshlet.MeshletIndex));
                stale++;
            }
        }

        report =
            new MeshPipelineWorkerReport(
                MeshPipelineCompletionKind.Applied,
                result.WorkerMilliseconds,
                accepted,
                stale);

        TryStartReadyWork();
        return true;
    }

    public ChunkPresentationIntegrationStats IntegratePublications(
        int maximumPerFrame,
        WorldFrameWorkBudget budget) =>
        _presentations.IntegrateFluidPublications(
            maximumPerFrame,
            budget);
}
