using Asteria.Core.World;

namespace Asteria.Client.Rendering;

public sealed class TerrainMeshPipeline
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly TerrainTextureLookup _textures;
    private readonly WorldUpdateQueue _updates;
    private readonly MeshletContentRevisions _revisions;
    private readonly ChunkPresentationController _presentations;
    private readonly int _maximumInteractiveMeshletsPerWorker;
    private readonly int _maximumBackgroundMeshletsPerWorker;
    private readonly TerrainMeshWorker _interactiveWorker = new();
    private readonly TerrainMeshWorker _backgroundWorker = new();

    private WorldMeshBatch? _interactiveInFlightBatch;
    private WorldMeshBatch? _backgroundInFlightBatch;

    public TerrainMeshPipeline(
        VoxelWorld world,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        WorldUpdateQueue updates,
        MeshletContentRevisions revisions,
        ChunkPresentationController presentations,
        int maximumInteractiveMeshletsPerWorker,
        int maximumBackgroundMeshletsPerWorker)
    {
        _world =
            world ??
            throw new ArgumentNullException(nameof(world));
        _blocks =
            blocks ??
            throw new ArgumentNullException(nameof(blocks));
        _textures =
            textures ??
            throw new ArgumentNullException(nameof(textures));
        _updates =
            updates ??
            throw new ArgumentNullException(nameof(updates));
        _revisions =
            revisions ??
            throw new ArgumentNullException(nameof(revisions));
        _presentations =
            presentations ??
            throw new ArgumentNullException(nameof(presentations));

        if (maximumInteractiveMeshletsPerWorker <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumInteractiveMeshletsPerWorker));
        }

        if (maximumBackgroundMeshletsPerWorker <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumBackgroundMeshletsPerWorker));
        }

        _maximumInteractiveMeshletsPerWorker =
            maximumInteractiveMeshletsPerWorker;
        _maximumBackgroundMeshletsPerWorker =
            maximumBackgroundMeshletsPerWorker;
    }

    public bool IsRunning =>
        _interactiveWorker.IsRunning ||
        _backgroundWorker.IsRunning;

    public bool TryStartReadyWork()
    {
        var started =
            TryStartLane(
                _interactiveWorker,
                ref _interactiveInFlightBatch,
                priority: true,
                _maximumInteractiveMeshletsPerWorker);

        started |=
            TryStartLane(
                _backgroundWorker,
                ref _backgroundInFlightBatch,
                priority: false,
                _maximumBackgroundMeshletsPerWorker);

        return started;
    }

    public bool TryPollCompleted(
        out MeshPipelineWorkerReport? report,
        out Exception? error)
    {
        if (TryPollLane(
                _interactiveWorker,
                ref _interactiveInFlightBatch,
                priority: true,
                out report,
                out error))
        {
            return true;
        }

        return TryPollLane(
            _backgroundWorker,
            ref _backgroundInFlightBatch,
            priority: false,
            out report,
            out error);
    }

    public ChunkPresentationIntegrationStats IntegratePublications(
        int maximumPerFrame,
        WorldFrameWorkBudget budget) =>
        _presentations.IntegrateTerrainPublications(
            maximumPerFrame,
            budget);

    private bool TryStartLane(
        TerrainMeshWorker worker,
        ref WorldMeshBatch? inFlightBatch,
        bool priority,
        int maximumMeshlets)
    {
        if (worker.IsRunning ||
            !(priority
                ? _updates.HasPriorityMeshWork
                : _updates.HasBackgroundMeshWork))
        {
            return false;
        }

        var drained =
            priority
                ? _updates.DrainPriorityMeshlets(
                    maximumMeshlets)
                : _updates.DrainBackgroundMeshlets(
                    maximumMeshlets);

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

        if (!worker.TryStart(
                _world,
                _blocks,
                _textures,
                batch,
                _revisions))
        {
            Requeue(batch, priority);
            return false;
        }

        inFlightBatch = batch;
        return true;
    }

    private bool TryPollLane(
        TerrainMeshWorker worker,
        ref WorldMeshBatch? inFlightBatch,
        bool priority,
        out MeshPipelineWorkerReport? report,
        out Exception? error)
    {
        report = null;
        error = null;

        if (!worker.TryTakeCompleted(
                out var result,
                out error))
        {
            return false;
        }

        var sourceBatch =
            inFlightBatch;
        inFlightBatch = null;

        if (error is not null)
        {
            if (sourceBatch is not null)
            {
                Requeue(
                    sourceBatch,
                    priority);
            }

            TryStartReadyWork();
            return true;
        }

        if (result is null)
        {
            if (sourceBatch is not null)
            {
                Requeue(
                    sourceBatch,
                    priority);
            }

            TryStartReadyWork();
            return true;
        }

        if (!_world.IsContentStampCurrent(
                result.ContentStamp))
        {
            Requeue(
                result.SourceBatch,
                priority);
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
                if (priority)
                {
                    _presentations
                        .EnqueuePriorityTerrainPublication(
                            meshlet,
                            revision,
                            result.ContentStamp);
                }
                else
                {
                    _presentations
                        .EnqueueTerrainPublication(
                            meshlet,
                            revision,
                            result.ContentStamp);
                }

                accepted++;
            }
            else
            {
                var mask =
                    ChunkMeshletMask.Single(
                        meshlet.MeshletIndex);

                if (priority)
                {
                    _updates.EnqueuePriorityMeshlets(
                        meshlet.Coord,
                        mask);
                }
                else
                {
                    _updates.EnqueueMeshlets(
                        meshlet.Coord,
                        mask);
                }

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

    private void Requeue(
        WorldMeshBatch batch,
        bool priority)
    {
        if (priority)
        {
            _updates.RequeuePriorityMeshlets(
                batch);
        }
        else
        {
            _updates.RequeueBackgroundMeshlets(
                batch);
        }
    }
}
