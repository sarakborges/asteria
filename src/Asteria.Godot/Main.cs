using System.Diagnostics;
using System.Text.Json;
using System.Threading.Tasks;
using Asteria.Client.Content;
using Asteria.Client.Gameplay;
using Asteria.Client.Rendering;
using Asteria.Core.World;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace Asteria.Client;

public partial class Main : Node3D
{
    private const float InteractionDistance = 6f;
    private const int RenderDistanceChunks = 4;
    private const int RetentionMarginChunks = 10;
    private const int MaxMaterializationTasksInFlight = 4;
    private const int MaxMaterializationDispatchesPerFrame = 4;
    private const int MaxMaterializationResultsPerFrame = 8;
    private const int MaxPresentationPublicationsPerFrame = 4;
    private const int MaxMeshletPublishesPerFrame = 4;
    private const int MaxChunkEvictionsPerFrame = 2;

    private readonly WorldUpdateQueue _worldUpdates = new();
    private readonly MeshletContentRevisions _contentRevisions = new();
    private readonly ChunkStreamingState _streaming = new();
    private readonly ChunkPresentationSelection _presentationSelection = new();
    private readonly Dictionary<ChunkCoord, ChunkPresentation>
        _presentations = [];
    private readonly Dictionary<ChunkCoord, Task<MaterializedChunkBuild>>
        _materializationTasks = [];
    private readonly Queue<MeshletPublication>
        _pendingMeshletPublications = new();

    private readonly VoxelWorld _world = new();

    private Task<MeshUpdateBuild>? _meshTask;
    private Task<LightingUpdateBuild>? _lightingTask;

    private BlockRegistry _blocks = null!;
    private FpsPlayer? _player;
    private Node _webUi = null!;
    private TerrainTextureCatalog _terrainTextures = null!;
    private TerrainTextureLookup _terrainTextureLookup = null!;
    private ShaderMaterial _terrainMaterial = null!;
    private BlockRuntimeId _placementBlock;

    private ChunkCoord _streamingCenter;
    private ulong _appliedPresentationSelectionRevision;
    private long _worldFrameDeadlineTimestamp;
    private bool _worldReadySent;

    public override void _Ready()
    {
        SetupWebUi();

        _blocks = BlockContentLoader.LoadProjectBlocks();
        _terrainTextures = TerrainTextureCatalog.Create(_blocks);
        _terrainTextureLookup = _terrainTextures.CreateLookup();
        _terrainMaterial = VoxelTerrainMaterial.Create(_terrainTextures);
        _placementBlock =
            _blocks.GetId(TestChunkFactory.StoneId);

        GD.Print(
            $"block content: loaded {_blocks.AuthoredCount} definitions, " +
            $"{_terrainTextures.TextureCount} terrain textures");
        GD.Print(
            $"streaming: render_distance={RenderDistanceChunks} " +
            $"retention_margin={RetentionMarginChunks} " +
            $"materialization_in_flight={MaxMaterializationTasksInFlight}");

        // Loading starts around the origin. The player is spawned only after
        // the center chunk presentation is fully published, so physics cannot
        // fall through an empty world while initial streaming catches up.
        _streamingCenter = ChunkCoord.Zero;
        SyncStreamingSelection();
    }

    public override void _Process(double delta)
    {
        BeginWorldFrameBudget(delta);

        SyncStreamingSelection();

        CollectMaterializationResults();
        DispatchMaterializationTasks();
        PublishPendingPresentations();

        PollMeshTask();
        IntegrateMeshletPublications();

        PollLightingTask();

        TryStartMeshTask();
        TryStartLightingTask();

        SyncPresentationVisibility();
        EvictDistantChunks();

        if (!_worldReadySent &&
            _presentations.TryGetValue(
                ChunkCoord.Zero,
                out var origin) &&
            origin.IsFullyPublished)
        {
            _worldReadySent = true;
            SetupPlayer();
            SendWorldReady();
        }
    }

    private void SetupWebUi()
    {
        _webUi = GetNode<Node>("WebUi");
        _webUi.Connect(
            "message_received",
            Callable.From<string>(OnWebUiMessage));
        _webUi.Connect(
            "webui_ready",
            Callable.From(OnWebUiReady));
    }

    private void OnWebUiReady()
    {
        SendCurrentState();
    }

    private void OnWebUiMessage(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);
            if (!document.RootElement.TryGetProperty(
                    "type",
                    out var typeElement))
            {
                return;
            }

            var type = typeElement.GetString();
            GD.Print($"webui -> godot: {type}");

            switch (type)
            {
                case "ui.ready":
                    SendCurrentState();
                    break;
                case "ui.ping":
                    SendWebUi(
                        "game.pong",
                        new { timestamp = Time.GetTicksMsec() });
                    break;
            }
        }
        catch (JsonException exception)
        {
            GD.PushWarning(
                $"Ignoring invalid WebUI message: {exception.Message}");
        }
    }

    private void SendCurrentState()
    {
        SendWebUi(
            "game.ready",
            new { bridge = 1, engine = "godot" });

        if (_worldReadySent)
        {
            SendWorldReady();
        }

        if (_player is not null)
        {
            SendWebUi(
                "game.player_ready",
                new { controller = "fps" });
            SendMouseCaptureState(
                _player.IsMouseCaptured);
        }
    }

    private void SendWorldReady()
    {
        SendWebUi(
            "game.chunk_ready",
            new
            {
                size = Chunk.Size,
                chunks = _world.ChunkCount,
                desiredChunks = _streaming.DesiredCount,
                retainedChunks = _streaming.RetainedCount,
                pendingChunks = _streaming.PendingCount,
                materializingChunks = _streaming.MaterializingCount,
                presentedChunks = _presentations.Count,
                blocks = _blocks.AuthoredCount,
                textures = _terrainTextures.TextureCount,
                meshletsPerChunk = ChunkMeshletMask.Count,
            });
    }

    private void SendWebUi(
        string type,
        object payload)
    {
        _webUi.Call(
            "post_message",
            JsonSerializer.Serialize(
                new { type, payload }));
    }

    private void SendMouseCaptureState(bool captured)
    {
        _webUi.Call(
            "set_mouse_captured",
            captured);
        SendWebUi(
            "game.mouse_capture",
            new { captured });
    }

    private void SetupPlayer()
    {
        if (_player is not null)
        {
            return;
        }

        _player = new FpsPlayer
        {
            Name = "Player",
            Position = new Vector3(0f, 20f, 0f),
        };

        _player.BreakRequested += BreakTargetBlock;
        _player.PlaceRequested += PlaceTargetBlock;
        _player.MouseCaptureChanged +=
            SendMouseCaptureState;
        AddChild(_player);

        SendWebUi(
            "game.player_ready",
            new { controller = "fps" });

        GD.Print(
            "streaming: origin presentation ready; player activated");
    }

    private void SyncStreamingSelection()
    {
        var center = CurrentStreamingCenter();
        var desired =
            ChunkStreamingSelection.DesiredQaChunks(
                center,
                RenderDistanceChunks,
                minimumChunkY: 0,
                maximumChunkY: 1);
        var retentionRadius =
            RenderDistanceChunks + RetentionMarginChunks;

        var changed = _streaming.RebuildSelection(
            center,
            RenderDistanceChunks,
            retentionRadius,
            desired);

        _presentationSelection.Sync(
            center,
            RenderDistanceChunks);

        if (!changed)
        {
            return;
        }

        _streamingCenter = center;
        _streaming.SyncResidentState(
            _world.LoadedChunkCoords,
            _presentations.Keys);

        GD.Print(
            $"streaming.selection center={center} " +
            $"desired={_streaming.DesiredCount} " +
            $"retained={_streaming.RetainedCount} " +
            $"pending={_streaming.PendingCount} " +
            $"movement={_streaming.MovementDirection}");
    }

    private ChunkCoord CurrentStreamingCenter()
    {
        if (_player is null)
        {
            return _streamingCenter;
        }

        var position = _player.GlobalPosition;
        var address = VoxelCoordinates.FromWorld(
            Mathf.FloorToInt(position.X),
            Math.Max(0, Mathf.FloorToInt(position.Y)),
            Mathf.FloorToInt(position.Z));

        return new ChunkCoord(
            address.Chunk.X,
            Math.Max(0, address.Chunk.Y),
            address.Chunk.Z);
    }

    private void CollectMaterializationResults()
    {
        if (_materializationTasks.Count == 0)
        {
            return;
        }

        var completed = _materializationTasks
            .Where(entry => entry.Value.IsCompleted)
            .OrderBy(entry =>
                ChunkStreamingState.Priority(
                    entry.Key,
                    _streamingCenter,
                    _streaming.MovementDirection))
            .Take(MaxMaterializationResultsPerFrame)
            .ToArray();

        var processed = 0;

        foreach (var (coord, task) in completed)
        {
            if (processed > 0 && WorldBudgetExhausted())
            {
                break;
            }

            processed++;
            _materializationTasks.Remove(coord);
            _streaming.FinishMaterializing(coord);

            if (task.IsFaulted)
            {
                GD.PushError(
                    task.Exception?.ToString() ??
                    $"Chunk materialization failed: {coord}");

                if (_streaming.IsDesired(coord))
                {
                    _streaming.EnqueuePending(coord);
                }

                continue;
            }

            if (task.IsCanceled)
            {
                if (_streaming.IsDesired(coord))
                {
                    _streaming.EnqueuePending(coord);
                }

                continue;
            }

            var result = task.Result;

            if (!_streaming.KeepsLoaded(coord))
            {
                continue;
            }

            if (_world.ContainsChunk(coord))
            {
                continue;
            }

            _world.InsertChunk(
                coord,
                result.Chunk);

            _streaming.EnqueuePresentation(coord);
            EnqueueChunkLightingReconciliation(coord);

            GD.Print(
                $"chunk.resident coord={coord} " +
                $"worker_ms={result.WorkerMilliseconds:F2} " +
                $"resident={_world.ChunkCount}");
        }

        if (processed > 0)
        {
            _streaming.SyncResidentState(
                _world.LoadedChunkCoords,
                _presentations.Keys);
        }
    }

    private void DispatchMaterializationTasks()
    {
        var dispatched = 0;

        while (_materializationTasks.Count <
                   MaxMaterializationTasksInFlight &&
               dispatched <
                   MaxMaterializationDispatchesPerFrame)
        {
            if (dispatched > 0 && WorldBudgetExhausted())
            {
                break;
            }

            var coord =
                _streaming.PopPendingByPriority();

            if (coord is null)
            {
                break;
            }

            if (!_streaming.IsDesired(coord.Value) ||
                _world.ContainsChunk(coord.Value) ||
                _materializationTasks.ContainsKey(coord.Value))
            {
                continue;
            }

            var selected = coord.Value;
            _streaming.MarkMaterializing(selected);

            _materializationTasks.Add(
                selected,
                Task.Run(() =>
                {
                    var stopwatch = Stopwatch.StartNew();
                    var chunk =
                        DeterministicChunkProvider.Materialize(
                            _blocks,
                            selected);

                    // Provisional local light prevents a newly resident chunk
                    // from presenting black while cross-chunk reconciliation
                    // catches up asynchronously.
                    ChunkLightingSolver.Initialize(
                        chunk,
                        _blocks);

                    stopwatch.Stop();
                    return new MaterializedChunkBuild(
                        selected,
                        chunk,
                        stopwatch.Elapsed.TotalMilliseconds);
                }));

            dispatched++;
        }
    }

    private void PublishPendingPresentations()
    {
        var published = 0;

        while (published <
                   MaxPresentationPublicationsPerFrame)
        {
            if (published > 0 && WorldBudgetExhausted())
            {
                break;
            }

            var coord =
                _streaming.PopPresentationByPriority(
                    _presentationSelection);

            if (coord is null)
            {
                break;
            }

            if (!_world.ContainsChunk(coord.Value) ||
                _presentations.ContainsKey(coord.Value))
            {
                continue;
            }

            var presentation =
                new ChunkPresentation(coord.Value);
            presentation.SetVisible(
                _presentationSelection.ShouldBeVisible(
                    coord.Value,
                    currentlyVisible: false));

            _presentations.Add(
                coord.Value,
                presentation);
            AddChild(presentation.Root);

            _worldUpdates.EnqueueMeshlets(
                coord.Value,
                ChunkMeshletMask.All);

            foreach (var (neighbor, meshlets) in
                     ChunkTopologyFrontier
                         .PresentedNeighborMeshlets(
                             coord.Value,
                             _presentations.ContainsKey))
            {
                _contentRevisions.Bump(
                    neighbor,
                    meshlets);
                _worldUpdates.EnqueueMeshlets(
                    neighbor,
                    meshlets);
            }

            published++;

            GD.Print(
                $"chunk.presentation.reserve coord={coord.Value} " +
                $"presented={_presentations.Count}");
        }
    }

    private void SyncPresentationVisibility()
    {
        if (_appliedPresentationSelectionRevision ==
            _presentationSelection.Revision)
        {
            return;
        }

        foreach (var (coord, presentation) in _presentations)
        {
            presentation.SetVisible(
                _presentationSelection.ShouldBeVisible(
                    coord,
                    presentation.IsVisible));
        }

        _appliedPresentationSelectionRevision =
            _presentationSelection.Revision;
    }

    private void EvictDistantChunks()
    {
        var maxEvictions =
            _streaming.HasRenderableBacklog
                ? 1
                : MaxChunkEvictionsPerFrame;
        var evicted = 0;
        var retentionRadius =
            RenderDistanceChunks + RetentionMarginChunks;

        while (evicted < maxEvictions)
        {
            if (evicted > 0 && WorldBudgetExhausted())
            {
                break;
            }

            var coord =
                _streaming.PopRetiredOutsideHorizontalRadius(
                    _streamingCenter,
                    retentionRadius);

            if (coord is null)
            {
                break;
            }

            if (_streaming.KeepsLoaded(coord.Value))
            {
                continue;
            }

            if (_presentations.Remove(
                    coord.Value,
                    out var presentation))
            {
                presentation.Retire();
            }

            _worldUpdates.RemoveMeshChunk(coord.Value);

            if (_world.TryRemoveChunk(
                    coord.Value,
                    out _))
            {
                foreach (var (neighbor, meshlets) in
                         ChunkTopologyFrontier
                             .PresentedNeighborMeshlets(
                                 coord.Value,
                                 _presentations.ContainsKey))
                {
                    _contentRevisions.Bump(
                        neighbor,
                        meshlets);
                    _worldUpdates.EnqueueMeshlets(
                        neighbor,
                        meshlets);
                }

                EnqueueChunkLightingReconciliation(
                    coord.Value);
            }

            _streaming.Forget(coord.Value);
            evicted++;

            GD.Print(
                $"chunk.unload coord={coord.Value} " +
                $"resident={_world.ChunkCount} " +
                $"presented={_presentations.Count}");
        }
    }

    private void EnqueueChunkLightingReconciliation(
        ChunkCoord coord)
    {
        foreach (var position in
                 ChunkTopologyFrontier.LightingSeeds(coord))
        {
            if (_world.IsLoadedAt(position))
            {
                _worldUpdates.EnqueueLighting(position);
            }
        }
    }

    private void BreakTargetBlock()
    {
        if (!TryGetTarget(
                out var hitVoxel,
                out _))
        {
            return;
        }

        var cell = _world.GetCellOrEmpty(hitVoxel);
        if (cell.IsEmpty)
        {
            return;
        }

        _placementBlock = cell.Block;

        if (_world.SetBlockAt(
                hitVoxel,
                BlockRuntimeId.Air,
                out var edit))
        {
            QueueVoxelEdit(edit.Position);
        }
    }

    private void PlaceTargetBlock()
    {
        if (!TryGetTarget(
                out var hitVoxel,
                out var faceOffset))
        {
            return;
        }

        var target = hitVoxel + faceOffset;

        if (!_world.IsLoadedAt(target) ||
            !_world.GetCellOrEmpty(target).IsEmpty)
        {
            return;
        }

        var worldVoxelMin = new Vector3(
            target.X,
            target.Y,
            target.Z);

        if (_player!.IntersectsVoxelAabb(
                worldVoxelMin))
        {
            return;
        }

        if (_world.SetBlockAt(
                target,
                _placementBlock,
                out var edit))
        {
            QueueVoxelEdit(edit.Position);
        }
    }

    private void QueueVoxelEdit(
        WorldVoxelCoord position)
    {
        _contentRevisions.BumpVoxelEdit(
            _world,
            position);
        _worldUpdates.EnqueueVoxelEdit(
            _world,
            position);

        TryStartMeshTask();
        TryStartLightingTask();
    }

    private bool TryGetTarget(
        out WorldVoxelCoord hitVoxel,
        out (int X, int Y, int Z) faceOffset)
    {
        hitVoxel = default;
        faceOffset = default;

        if (_player is null)
        {
            return false;
        }

        var (from, to) =
            _player.GetInteractionRay(
                InteractionDistance);
        var direction = to - from;

        var hit = VoxelWorldRaycaster.Raycast(
            _world,
            _blocks,
            new NVector3(from.X, from.Y, from.Z),
            new NVector3(
                direction.X,
                direction.Y,
                direction.Z),
            InteractionDistance);

        if (hit is null ||
            !hit.Value.HasSurfaceNormal)
        {
            return false;
        }

        hitVoxel = hit.Value.Voxel;
        faceOffset = (
            hit.Value.NormalX,
            hit.Value.NormalY,
            hit.Value.NormalZ);
        return true;
    }

    private void TryStartMeshTask()
    {
        if (_meshTask is not null ||
            !_worldUpdates.HasMeshWork)
        {
            return;
        }

        var drained =
            _worldUpdates.DrainMeshlets();

        if (drained.IsEmpty)
        {
            return;
        }

        var filtered =
            drained.DirtyMeshlets
                .Where(entry =>
                    _presentations.ContainsKey(entry.Key) &&
                    _world.ContainsChunk(entry.Key))
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value);

        if (filtered.Count == 0)
        {
            return;
        }

        var batch =
            new WorldMeshBatch(filtered);
        var snapshot =
            _world.CloneMeshNeighborhood(
                batch.DirtyMeshlets.Keys);
        var revisions =
            _contentRevisions.Capture(
                batch.DirtyMeshlets);
        var blocks = _blocks;
        var textures = _terrainTextureLookup;

        _meshTask = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var meshlets = BuildMeshlets(
                snapshot,
                blocks,
                textures,
                batch.DirtyMeshlets);
            stopwatch.Stop();

            return new MeshUpdateBuild(
                batch,
                revisions,
                meshlets,
                stopwatch.Elapsed.TotalMilliseconds);
        });
    }

    private void PollMeshTask()
    {
        if (_meshTask is null ||
            !_meshTask.IsCompleted)
        {
            return;
        }

        if (_meshTask.IsFaulted)
        {
            GD.PushError(
                _meshTask.Exception?.ToString() ??
                "Meshlet geometry task failed.");
            _meshTask = null;
            return;
        }

        var result = _meshTask.Result;
        _meshTask = null;
        var accepted = 0;
        var stale = 0;

        foreach (var meshlet in result.Meshlets)
        {
            if (!_world.ContainsChunk(meshlet.Coord) ||
                !_presentations.ContainsKey(meshlet.Coord))
            {
                continue;
            }

            var key = new ChunkMeshletKey(
                meshlet.Coord,
                meshlet.MeshletIndex);
            var revision =
                result.ContentRevisions[key];

            if (_contentRevisions.IsCurrent(
                    key,
                    revision))
            {
                _pendingMeshletPublications.Enqueue(
                    new MeshletPublication(
                        meshlet,
                        revision));
                accepted++;
            }
            else
            {
                _worldUpdates.EnqueueMeshlets(
                    meshlet.Coord,
                    ChunkMeshletMask.Single(
                        meshlet.MeshletIndex));
                stale++;
            }
        }

        GD.Print(
            $"world.geometry worker_ms=" +
            $"{result.WorkerMilliseconds:F2} " +
            $"accepted={accepted} stale={stale}");

        TryStartMeshTask();
    }

    private void TryStartLightingTask()
    {
        if (_lightingTask is not null ||
            !_worldUpdates.HasLightingWork)
        {
            return;
        }

        var batch = _worldUpdates.DrainLighting();
        if (batch.IsEmpty)
        {
            return;
        }

        var worldRevision = _world.Revision;
        var snapshot = _world.CloneForWorker();
        var blocks = _blocks;

        _lightingTask = Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var lighting =
                VoxelWorldLightingSolver.RelightAfterEdits(
                    snapshot,
                    blocks,
                    batch.EditedPositions);
            stopwatch.Stop();

            return new LightingUpdateBuild(
                worldRevision,
                snapshot,
                batch,
                lighting,
                stopwatch.Elapsed.TotalMilliseconds);
        });
    }

    private void PollLightingTask()
    {
        if (_lightingTask is null ||
            !_lightingTask.IsCompleted)
        {
            return;
        }

        if (_lightingTask.IsFaulted)
        {
            GD.PushError(
                _lightingTask.Exception?.ToString() ??
                "Incremental lighting task failed.");
            _lightingTask = null;
            return;
        }

        var result = _lightingTask.Result;
        _lightingTask = null;

        if (result.WorldRevision !=
            _world.Revision)
        {
            _worldUpdates.RequeueLighting(
                result.SourceBatch);
            TryStartLightingTask();
            return;
        }

        _world.CopyLightFrom(
            result.LightingSnapshot);

        foreach (var position in
                 result.Lighting.ChangedPositions)
        {
            _worldUpdates.EnqueueVoxelMeshlets(
                _world,
                position);
        }

        GD.Print(
            $"world.lighting worker_ms=" +
            $"{result.WorkerMilliseconds:F2} " +
            $"light_changes=" +
            $"{result.Lighting.ChangedPositions.Count} " +
            $"light_processed=" +
            $"{result.Lighting.ProcessedVoxelCount}");

        TryStartMeshTask();
        TryStartLightingTask();
    }

    private void IntegrateMeshletPublications()
    {
        var stopwatch = Stopwatch.StartNew();
        var published = 0;
        var stale = 0;

        while (published + stale <
                   MaxMeshletPublishesPerFrame &&
               _pendingMeshletPublications.Count > 0)
        {
            if (published + stale > 0 &&
                WorldBudgetExhausted())
            {
                break;
            }

            var pending =
                _pendingMeshletPublications.Dequeue();
            var key = new ChunkMeshletKey(
                pending.Meshlet.Coord,
                pending.Meshlet.MeshletIndex);

            if (!_world.ContainsChunk(
                    pending.Meshlet.Coord) ||
                !_presentations.TryGetValue(
                    pending.Meshlet.Coord,
                    out var presentation))
            {
                continue;
            }

            if (!_contentRevisions.IsCurrent(
                    key,
                    pending.ContentRevision))
            {
                _worldUpdates.EnqueueMeshlets(
                    pending.Meshlet.Coord,
                    ChunkMeshletMask.Single(
                        pending.Meshlet.MeshletIndex));
                stale++;
                continue;
            }

            presentation.Apply(
                pending.Meshlet.MeshletIndex,
                pending.Meshlet.Data,
                _terrainMaterial);

            published++;
        }

        stopwatch.Stop();

        if (published > 0 || stale > 0)
        {
            GD.Print(
                $"world.meshlets published={published} " +
                $"stale={stale} " +
                $"remaining={_pendingMeshletPublications.Count} " +
                $"publish_ms={stopwatch.Elapsed.TotalMilliseconds:F2}");
        }
    }

    private static List<MeshletBuild> BuildMeshlets(
        VoxelWorld world,
        BlockRegistry blocks,
        TerrainTextureLookup textures,
        IReadOnlyDictionary<ChunkCoord, ChunkMeshletMask> dirty)
    {
        var result = new List<MeshletBuild>();

        foreach (var (coord, mask) in dirty
                     .OrderBy(entry => entry.Key.Y)
                     .ThenBy(entry => entry.Key.Z)
                     .ThenBy(entry => entry.Key.X))
        {
            if (!world.ContainsChunk(coord))
            {
                continue;
            }

            foreach (var meshletIndex in mask.Indices())
            {
                result.Add(
                    new MeshletBuild(
                        coord,
                        meshletIndex,
                        ChunkMeshDataBuilder.BuildMeshlet(
                            world,
                            coord,
                            blocks,
                            textures,
                            meshletIndex)));
            }
        }

        return result;
    }

    private void BeginWorldFrameBudget(double delta)
    {
        var seconds = (float)Math.Max(delta, 0.0001);
        var milliseconds = seconds > 1f / 55f
            ? 2.0
            : seconds > 1f / 70f
                ? 3.0
                : 4.0;

        var ticks =
            (long)(Stopwatch.Frequency *
                   milliseconds /
                   1000.0);

        _worldFrameDeadlineTimestamp =
            Stopwatch.GetTimestamp() + ticks;
    }

    private bool WorldBudgetExhausted() =>
        Stopwatch.GetTimestamp() >=
        _worldFrameDeadlineTimestamp;

    private sealed record MaterializedChunkBuild(
        ChunkCoord Coord,
        Chunk Chunk,
        double WorkerMilliseconds);

    private sealed record MeshletBuild(
        ChunkCoord Coord,
        int MeshletIndex,
        ChunkMeshData Data);

    private sealed record MeshletPublication(
        MeshletBuild Meshlet,
        ulong ContentRevision);

    private sealed record MeshUpdateBuild(
        WorldMeshBatch SourceBatch,
        IReadOnlyDictionary<ChunkMeshletKey, ulong>
            ContentRevisions,
        IReadOnlyList<MeshletBuild> Meshlets,
        double WorkerMilliseconds);

    private sealed record LightingUpdateBuild(
        ulong WorldRevision,
        VoxelWorld LightingSnapshot,
        WorldLightingBatch SourceBatch,
        VoxelLightingUpdateResult Lighting,
        double WorkerMilliseconds);
}
