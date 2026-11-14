using Asteria.Core.World;

namespace Asteria.Client.Rendering;

public sealed class TerrainMeshPipeline
{
    private readonly VoxelWorld _world;
    private readonly BlockRegistry _blocks;
    private readonly TerrainTextureLookup _textures;
    private readonly BiomeTintField _biomeTints;
    private readonly AttachedLayerRegistry _layers;
    private readonly DyeRegistry _dyes;
    private readonly WorldUpdateQueue _updates;
    private readonly MeshletContentRevisions _revisions;
    private readonly ChunkPresentationController _presentations;
    private readonly int _maximumInteractiveMeshletsPerWorker;
    private readonly int _maximumBackgroundMeshletsPerWorker;
    private readonly TerrainMeshWorker _interactiveWorker;
    private readonly TerrainMeshWorker _backgroundWorker;

    private bool _acceptingWork = true;
    private bool _preferInteractive = true;
    private WorldMeshBatch? _interactiveInFlightBatch;
    private WorldMeshBatch? _backgroundInFlightBatch;

    public TerrainMeshPipeline(
        VoxelWorld world,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        BiomeTintField biomeTints,
        AttachedLayerRegistry layers,
        DyeRegistry dyes,
        WorldUpdateQueue updates,
        MeshletContentRevisions revisions,
        ChunkPresentationController presentations,
        int maximumInteractiveMeshletsPerWorker,
        int maximumBackgroundMeshletsPerWorker,
        WorldWorkerPool workerPool)
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
        _biomeTints =
            biomeTints ??
            throw new ArgumentNullException(nameof(biomeTints));
        _layers = layers ?? throw new ArgumentNullException(nameof(layers));
        _dyes = dyes ?? throw new ArgumentNullException(nameof(dyes));
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
        ArgumentNullException.ThrowIfNull(workerPool);
        _interactiveWorker = new TerrainMeshWorker(workerPool);
        _backgroundWorker = new TerrainMeshWorker(workerPool);
    }

    public bool IsRunning =>
        _interactiveWorker.IsRunning ||
        _backgroundWorker.IsRunning;

    public int ActiveWorkerCount =>
        (_interactiveWorker.IsRunning ? 1 : 0) +
        (_backgroundWorker.IsRunning ? 1 : 0);

    public bool HasWorkAffecting(Func<ChunkCoord, bool> isRelevantChunk)
    {
        ArgumentNullException.ThrowIfNull(isRelevantChunk);
        if (_updates.HasMeshWorkInChunks(isRelevantChunk) ||
            HasRelevantChunk(_interactiveInFlightBatch, isRelevantChunk) ||
            HasRelevantChunk(_backgroundInFlightBatch, isRelevantChunk))
            return true;

        return (_interactiveWorker.IsRunning && _interactiveInFlightBatch is null) ||
            (_backgroundWorker.IsRunning && _backgroundInFlightBatch is null);
    }

    private static bool HasRelevantChunk(
        WorldMeshBatch? batch,
        Func<ChunkCoord, bool> isRelevantChunk) =>
        batch is not null &&
        batch.DirtyMeshlets.Keys.Any(isRelevantChunk);

    public void BeginRetirement()
    {
        _acceptingWork = false;
    }

    public bool TryStartReadyWork(
        bool underStreamingPressure = false,
        ChunkCoord? center = null,
        ChunkMovementDirection movement = default,
        bool allowBackground = true)
    {
        if (!_acceptingWork)
        {
            return false;
        }

        UpdateInFlightWorkerPriorities(center, movement);

        // Interactive and background operations enter the same global
        // priority queue. Alternate capture lanes for fairness; the shared
        // executor decides which queued operation gets a physical worker.
        var mayStartBackground = allowBackground;

        // Worker startup captures neighboring chunks synchronously.
        // Limit each call to one lane and alternate to avoid starvation.
        var started = _preferInteractive
            ? TryStartLane(
                  _interactiveWorker, ref _interactiveInFlightBatch,
                  priority: true, _maximumInteractiveMeshletsPerWorker,
                  center, movement) ||
              (mayStartBackground && TryStartLane(
                  _backgroundWorker, ref _backgroundInFlightBatch,
                  priority: false, _maximumBackgroundMeshletsPerWorker))
            : (mayStartBackground && TryStartLane(
                  _backgroundWorker, ref _backgroundInFlightBatch,
                  priority: false, _maximumBackgroundMeshletsPerWorker)) ||
              TryStartLane(
                  _interactiveWorker, ref _interactiveInFlightBatch,
                  priority: true, _maximumInteractiveMeshletsPerWorker,
                  center, movement);

        if (started)
            _preferInteractive = !_preferInteractive;
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

    private void UpdateInFlightWorkerPriorities(
        ChunkCoord? center,
        ChunkMovementDirection movement)
    {
        if (center is null)
            return;

        if (_interactiveInFlightBatch is { } interactive)
        {
            var priority = ResolveWorkerPriority(
                interactive, center, movement,
                WorldWorkerPool.InteractiveWorldPriority);
            _interactiveWorker.TryUpdatePriority(priority);
        }

        if (_backgroundInFlightBatch is { } background)
        {
            var priority = ResolveWorkerPriority(
                background, center, movement,
                WorldWorkerPool.BackgroundWorldPriority);
            _backgroundWorker.TryUpdatePriority(priority);
        }
    }

    private static int ResolveWorkerPriority(
        WorldMeshBatch batch,
        ChunkCoord? center,
        ChunkMovementDirection movement,
        int fallbackPriority)
    {
        if (center is not { } focus || batch.DirtyMeshlets.Count == 0)
            return fallbackPriority;

        var nearestBand = batch.DirtyMeshlets.Keys
            .Min(coord => ChunkStreamingState.StreamingPriority(
                coord, focus, movement).StreamingBand);
        return checked(nearestBand * 10);
    }

    private bool TryStartLane(
        TerrainMeshWorker worker,
        ref WorldMeshBatch? inFlightBatch,
        bool priority,
        int maximumMeshlets,
        ChunkCoord? center = null,
        ChunkMovementDirection movement = default)
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
                    maximumMeshlets, center, movement,
                    MeshWorkerSnapshotLimits.MaximumSourceChunksPerBatch)
                : _updates.DrainBackgroundMeshlets(
                    maximumMeshlets,
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

        var relightable = _presentations.CaptureRelightableTerrain(batch);
        if (!worker.TryStart(
                _world,
                _blocks,
                _textures,
                _biomeTints,
                _layers,
                _dyes,
                batch,
                _revisions,
                relightable,
                workerPriority: ResolveWorkerPriority(
                    batch, center, movement,
                    priority
                        ? WorldWorkerPool.InteractiveWorldPriority
                        : WorldWorkerPool.BackgroundWorldPriority)))
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
            return true;
        }

        var batchStale = !result.Dependencies.IsCurrent(_world);
        var accepted = 0;
        var stale = 0;
        var salvaged = 0;
        var relitWithoutMeshing = 0;

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
            var dependencies = result.MeshletDependencies[key];

            var needsPriority = priority ||
                _presentations.NeedsPriorityInitialTerrainPublication(
                    meshlet.Coord, meshlet.MeshletIndex);

            if (_revisions.IsCurrent(
                    key,
                    revision) &&
                dependencies.IsCurrent(_world))
            {
                if (needsPriority)
                {
                    _presentations
                        .EnqueuePriorityTerrainPublication(
                            meshlet,
                            revision,
                            dependencies);
                }
                else
                {
                    _presentations
                        .EnqueueTerrainPublication(
                            meshlet,
                            revision,
                            dependencies);
                }

                accepted++;
                if (batchStale)
                    salvaged++;
                if (meshlet.LightOnly)
                    relitWithoutMeshing++;
            }
            else
            {
                var mask =
                    ChunkMeshletMask.Single(
                        meshlet.MeshletIndex);

                if (needsPriority)
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
                accepted == 0 && stale > 0
                    ? MeshPipelineCompletionKind.RequeuedStale
                    : MeshPipelineCompletionKind.Applied,
                result.WorkerMilliseconds,
                accepted,
                stale,
                result.CaptureMilliseconds,
                relitWithoutMeshing,
                salvaged);
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
