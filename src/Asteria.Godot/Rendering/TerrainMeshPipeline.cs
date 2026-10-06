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
    private readonly int _maximumMeshletsPerWorker;
    private readonly TerrainMeshWorker _worker = new();

    private WorldMeshBatch? _inFlightBatch;

    public TerrainMeshPipeline(
        VoxelWorld world,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        WorldUpdateQueue updates,
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
            !_updates.HasMeshWork)
        {
            return false;
        }

        var drained =
            _updates.DrainMeshlets(
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
                _textures,
                batch,
                _revisions))
        {
            _updates.RequeueMeshlets(batch);
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
                _updates.RequeueMeshlets(
                    sourceBatch);
            }

            TryStartReadyWork();
            return true;
        }

        if (result is null)
        {
            if (sourceBatch is not null)
            {
                _updates.RequeueMeshlets(
                    sourceBatch);
            }

            TryStartReadyWork();
            return true;
        }

        if (!_world.IsContentStampCurrent(
                result.ContentStamp))
        {
            _updates.RequeueMeshlets(
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
                _presentations.EnqueueTerrainPublication(
                    meshlet,
                    revision,
                    result.ContentStamp);
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
        _presentations.IntegrateTerrainPublications(
            maximumPerFrame,
            budget);
}
