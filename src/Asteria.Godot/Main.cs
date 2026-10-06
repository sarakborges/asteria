using System.Diagnostics;
using System.Text.Json;
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
    private const uint WorldTicksPerSecond = 40;
    private const int RenderDistanceChunks = 4;
    private const int RetentionMarginChunks = 10;
    private const int MaxMaterializationTasksInFlight = 4;
    private const int MaxMaterializationDispatchesPerFrame = 4;
    private const int MaxMaterializationResultsPerFrame = 8;
    private const int MaxPresentationPublicationsPerFrame = 4;
    private const int MaxMeshletPublishesPerFrame = 4;
    private const int MaxFluidMeshletPublishesPerFrame = 4;
    private const int MaxFluidUpdatesPerWorker = 512;
    private const int MaxChunkEvictionsPerFrame = 2;
    private const double WorldGravityStrength = 18.0;

    private readonly WorldUpdateQueue _worldUpdates = new();
    private readonly WorldTickClock _worldTicks = new();
    private readonly FluidUpdateQueue _fluidUpdates = new();
    private readonly FluidMeshUpdateQueue _fluidMeshUpdates = new();
    private readonly BlockPhysicsUpdateQueue _blockPhysicsUpdates = new();
    private readonly MeshletContentRevisions _contentRevisions = new();
    private readonly MeshletContentRevisions _fluidContentRevisions = new();
    private ChunkResidencyRuntime _residency = null!;
    private ChunkPresentationController _chunkPresentations = null!;

    private readonly VoxelWorld _world = new();
    private VoxelMutationRuntime _mutations = null!;
    private BlockPhysicsRuntime _blockPhysics = null!;
    private DroppedBlockRuntime _droppedBlocks = null!;
    private BlockInteractionRuntime _blockInteractions = null!;
    private BlockEntityPresentationController _blockEntityPresentations = null!;

    private readonly TerrainMeshWorker _terrainMeshWorker = new();
    private readonly FluidMeshWorker _fluidMeshWorker = new();
    private readonly FluidSimulationWorker _fluidSimulationWorker = new();
    private readonly LightingWorker _lightingWorker = new();
    private LightingResultIntegrator _lightingIntegration = null!;

    private BlockRegistry _blocks = null!;
    private FluidRegistry _fluids = null!;
    private FpsPlayer? _player;
    private Node _webUi = null!;
    private TerrainTextureCatalog _terrainTextures = null!;
    private TerrainTextureLookup _terrainTextureLookup = null!;
    private VoxelTerrainMaterialSet _terrainMaterials = null!;
    private FluidMaterialCatalog _fluidMaterials = null!;
    private BlockRuntimeId _placementBlock;

    private WorldFrameWorkBudget _worldFrameBudget;
    private bool _worldReadySent;

    public override void _Ready()
    {
        SetupWebUi();

        _blocks = BlockContentLoader.LoadProjectBlocks();
        _fluids = FluidContentLoader.LoadProjectFluids();
        _droppedBlocks =
            new DroppedBlockRuntime(
                _world,
                _blocks);
        _mutations = new VoxelMutationRuntime(
            _world,
            _worldUpdates,
            _fluidUpdates,
            _fluidMeshUpdates,
            _blockPhysicsUpdates,
            _contentRevisions,
            _fluidContentRevisions);
        _lightingIntegration =
            new LightingResultIntegrator(
                _world,
                _worldUpdates,
                _fluidMeshUpdates,
                _contentRevisions,
                _fluidContentRevisions);
        _blockPhysics = new BlockPhysicsRuntime(
            _world,
            _blocks,
            _mutations,
            _blockPhysicsUpdates,
            _droppedBlocks);
        _blockInteractions =
            new BlockInteractionRuntime(
                _world,
                _blocks,
                _mutations,
                _droppedBlocks);
        _terrainTextures = TerrainTextureCatalog.Create(_blocks);
        _terrainTextureLookup = _terrainTextures.CreateLookup();
        _terrainMaterials = VoxelTerrainMaterialSet.Create(_terrainTextures);
        _fluidMaterials = FluidMaterialCatalog.Create(_fluids);
        _blockEntityPresentations =
            new BlockEntityPresentationController(
                this,
                _blocks,
                _fluids,
                _terrainTextureLookup,
                _terrainMaterials);
        _residency =
            new ChunkResidencyRuntime(
                _world,
                _blocks,
                _fluids,
                _worldUpdates,
                _fluidUpdates,
                _fluidMeshUpdates,
                _blockPhysics,
                _contentRevisions,
                _fluidContentRevisions,
                _worldTicks,
                new ChunkResidencySettings(
                    MaxMaterializationTasksInFlight,
                    MaxMaterializationDispatchesPerFrame,
                    MaxMaterializationResultsPerFrame,
                    MaxChunkEvictionsPerFrame,
                    WorldTicksPerSecond));
        _chunkPresentations =
            new ChunkPresentationController(
                this,
                _world,
                _worldUpdates,
                _fluidMeshUpdates,
                _contentRevisions,
                _fluidContentRevisions,
                _terrainMaterials,
                _fluidMaterials);
        _placementBlock =
            _blocks.GetId(TestChunkFactory.StoneId);

        GD.Print(
            $"block content: loaded {_blocks.AuthoredCount} definitions, " +
            $"{_terrainTextures.TextureCount} terrain textures");
        GD.Print(
            $"fluid content: loaded {_fluids.AuthoredCount} definitions");
        GD.Print(
            $"streaming: render_distance={RenderDistanceChunks} " +
            $"retention_margin={RetentionMarginChunks} " +
            $"materialization_in_flight={MaxMaterializationTasksInFlight}");

        // Loading starts around the origin. The player is spawned only after
        // the center chunk presentation is fully published, so physics cannot
        // fall through an empty world while initial streaming catches up.
        SyncStreamingSelection();
    }

    public override void _Process(double delta)
    {
        _worldTicks.Advance(
            delta,
            WorldTicksPerSecond);
        BeginWorldFrameBudget(delta);

        SyncStreamingSelection();

        CollectMaterializationResults();
        DispatchMaterializationTasks();
        PublishPendingPresentations();
        ProcessBlockPhysics(delta);

        PollFluidWorker();
        PollFluidMeshWorker();
        IntegrateFluidMeshletPublications();

        PollTerrainMeshWorker();
        IntegrateMeshletPublications();

        PollLightingWorker();

        TryStartFluidWorker();
        TryStartFluidMeshWorker();
        TryStartTerrainMeshWorker();
        TryStartLightingWorker();

        SyncPresentationVisibility();
        EvictDistantChunks();

        if (!_worldReadySent &&
            _chunkPresentations.IsFullyPublished(
                ChunkCoord.Zero))
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
                desiredChunks = _residency.DesiredCount,
                retainedChunks = _residency.RetainedCount,
                pendingChunks = _residency.PendingCount,
                materializingChunks = _residency.MaterializingCount,
                presentedChunks = _chunkPresentations.Count,
                archivedChunks = _world.ArchivedChunkCount,
                dirtyChunks = _world.DirtyChunkCount,
                blocks = _blocks.AuthoredCount,
                fluids = _fluids.AuthoredCount,
                fluidUpdates = _fluidUpdates.Count,
                fluidScheduled = _fluidUpdates.ScheduledCount,
                fluidDormantChunks = _fluidUpdates.DormantChunkCount,
                fallingBlocks = _blockPhysics.ActiveCount,
                droppedBlocks = _droppedBlocks.ActiveCount,
                physicsUpdates = _blockPhysicsUpdates.Count,
                worldTick = _worldTicks.CurrentTick,
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

        _player.FluidContactProvider =
            (bounds, eyeY) =>
                FluidBodyQuery.Sample(
                    _world,
                    bounds,
                    eyeY);
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
            RenderDistanceChunks +
            RetentionMarginChunks;

        var changed =
            _residency.SyncSelection(
                center,
                RenderDistanceChunks,
                retentionRadius,
                desired,
                _chunkPresentations.Coordinates);

        if (!changed)
        {
            return;
        }

        GD.Print(
            $"streaming.selection center={center} " +
            $"desired={_residency.DesiredCount} " +
            $"retained={_residency.RetainedCount} " +
            $"pending={_residency.PendingCount} " +
            $"movement={_residency.MovementDirection}");
    }

    private ChunkCoord CurrentStreamingCenter()
    {
        if (_player is null)
        {
            return _residency.Center;
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
        ReportResidencyUpdate(
            _residency.CollectMaterializationResults(
                _worldFrameBudget,
                _chunkPresentations.Coordinates));
    }

    private void DispatchMaterializationTasks()
    {
        ReportResidencyUpdate(
            _residency.DispatchMaterializationTasks(
                _worldFrameBudget,
                _chunkPresentations.Coordinates));
    }

    private void ReportResidencyUpdate(
        ChunkResidencyUpdate update)
    {
        foreach (var failure in update.Failures)
        {
            GD.PushError(
                $"Chunk materialization failed: " +
                $"{failure.Coord}\n{failure.Error}");
        }

        foreach (var activation in
                 update.Activations)
        {
            GD.Print(
                $"chunk.resident coord={activation.Coord} " +
                $"source={activation.Source.ToString().ToLowerInvariant()} " +
                $"worker_ms={activation.WorkerMilliseconds:F2} " +
                $"resident={_world.ChunkCount} " +
                $"archived={_world.ArchivedChunkCount}");
        }
    }

    private void PublishPendingPresentations()
    {
        var reserved =
            _chunkPresentations.ReservePending(
                _residency,
                MaxPresentationPublicationsPerFrame,
                _worldFrameBudget);

        if (reserved > 0)
        {
            _residency.SyncResidentState(
                _chunkPresentations.Coordinates);

            GD.Print(
                $"chunk.presentation.reserve count={reserved} " +
                $"presented={_chunkPresentations.Count}");
        }
    }

    private void SyncPresentationVisibility()
    {
        _chunkPresentations.SyncVisibility(
            _residency);
    }

    private void EvictDistantChunks()
    {
        foreach (var retirement in
                 _residency.RetireDistantChunks(
                     _worldFrameBudget))
        {
            _chunkPresentations.Retire(
                retirement.Coord);

            GD.Print(
                $"chunk.unload coord={retirement.Coord} " +
                $"archive={retirement.ArchiveResult} " +
                $"resident={_world.ChunkCount} " +
                $"archived={_world.ArchivedChunkCount} " +
                $"presented={_chunkPresentations.Count}");
        }
    }

    private void BreakTargetBlock()
    {
        if (!TryGetTarget(
                out var hit))
        {
            return;
        }

        var decision =
            _blockInteractions.Break(hit);

        if (!decision.Accepted)
        {
            return;
        }

        _placementBlock =
            decision.Cell.Block;
        KickWorldMutationWorkers();
    }

    private void PlaceTargetBlock()
    {
        if (!TryGetTarget(
                out var hit))
        {
            return;
        }

        var decision =
            _blockInteractions.Place(
                hit,
                new VoxelCell(_placementBlock),
                _player!.CollisionBounds);

        if (!decision.Accepted)
        {
            return;
        }

        KickWorldMutationWorkers();
    }

    private void KickWorldMutationWorkers()
    {
        TryStartFluidWorker();
        TryStartFluidMeshWorker();
        TryStartTerrainMeshWorker();
        TryStartLightingWorker();
    }

    private void ProcessBlockPhysics(double delta)
    {
        var wake =
            BlockPhysicsWakeResult.Empty;

        if (_worldTicks.TicksThisFrame > 0)
        {
            wake =
                _blockPhysics.ProcessWakeups();
        }

        var landed =
            _blockPhysics.Advance(
                delta,
                WorldGravityStrength);
        var dropped =
            _droppedBlocks.Advance(
                delta,
                WorldGravityStrength);

        _blockEntityPresentations.Sync(
            _blockPhysics.ActiveBlocks,
            _droppedBlocks.ActiveBlocks);

        if (wake.HasChanges ||
            landed > 0 ||
            dropped.Settled > 0 ||
            dropped.Expired > 0)
        {
            GD.Print(
                $"world.block_physics falling_started=" +
                $"{wake.FallingStarted} " +
                $"unsupported_removed=" +
                $"{wake.UnsupportedRemoved} " +
                $"landed={landed} " +
                $"falling_active={_blockPhysics.ActiveCount} " +
                $"drops_active={_droppedBlocks.ActiveCount} " +
                $"drops_settled={dropped.Settled} " +
                $"drops_expired={dropped.Expired} " +
                $"queued={_blockPhysicsUpdates.Count}");
        }
    }

    private bool TryGetTarget(
        out VoxelWorldHit hit)
    {
        hit = default;

        if (_player is null)
        {
            return false;
        }

        var (from, to) =
            _player.GetInteractionRay(
                InteractionDistance);
        var direction = to - from;

        var resolved =
            VoxelWorldRaycaster.Raycast(
                _world,
                _blocks,
                new NVector3(
                    from.X,
                    from.Y,
                    from.Z),
                new NVector3(
                    direction.X,
                    direction.Y,
                    direction.Z),
                InteractionDistance);

        if (resolved is null)
        {
            return false;
        }

        hit = resolved.Value;
        return true;
    }

    private void TryStartFluidWorker()
    {
        if (_fluidSimulationWorker.IsRunning ||
            !_fluidUpdates.HasReadyWork(
                _worldTicks.CurrentTick))
        {
            return;
        }

        var batch =
            _fluidUpdates.DrainReady(
                _worldTicks.CurrentTick,
                MaxFluidUpdatesPerWorker);

        if (batch.IsEmpty)
        {
            return;
        }

        _fluidSimulationWorker.TryStart(
            _world,
            _fluids,
            batch);
    }

    private void PollFluidWorker()
    {
        if (!_fluidSimulationWorker.TryTakeCompleted(
                out var result,
                out var error))
        {
            return;
        }

        if (error is not null)
        {
            GD.PushError(error.ToString());
            return;
        }

        if (result is null)
        {
            return;
        }

        if (!result.Dependencies.IsCurrent(
                _world))
        {
            _fluidUpdates.RequeueTopology(
                result.SourceBatch.TopologyPositions);
            _fluidUpdates.RequeueDue(
                result.SourceBatch.DueTicks,
                _worldTicks.CurrentTick);
            TryStartFluidWorker();
            return;
        }

        var applied =
            _mutations.ApplyFluidChanges(
                result.Simulation.Changes);

        if (!applied.Accepted)
        {
            _fluidUpdates.RequeueTopology(
                result.SourceBatch.TopologyPositions);
            _fluidUpdates.RequeueDue(
                result.SourceBatch.DueTicks,
                _worldTicks.CurrentTick);
            TryStartFluidWorker();
            return;
        }

        foreach (var dormant in
                 result.Simulation.DormantTicks)
        {
            _fluidUpdates.DeferUnloaded(
                dormant);
        }

        foreach (var request in
                 result.Simulation.ScheduleRequests)
        {
            ScheduleFluidRequest(request);
        }

        GD.Print(
            $"world.fluid tick={_worldTicks.CurrentTick} " +
            $"worker_ms={result.WorkerMilliseconds:F2} " +
            $"processed={result.Simulation.ProcessedVoxelCount} " +
            $"changes={applied.AppliedChangeCount} " +
            $"changed_voxels={applied.UniquePositionCount} " +
            $"scheduled={result.Simulation.ScheduleRequests.Count} " +
            $"downhill_searches={result.Simulation.DownhillSearchCount} " +
            $"downhill_nodes={result.Simulation.DownhillVisitedNodeCount} " +
            $"backlog={_fluidUpdates.Count}");

        TryStartFluidWorker();
        TryStartFluidMeshWorker();
    }

    private void ScheduleFluidRequest(
        FluidScheduleRequest request)
    {
        var delay =
            FluidTiming.DelayTicks(
                _fluids,
                request.Fluid,
                WorldTicksPerSecond);

        if (delay is null)
        {
            return;
        }

        var dueTick =
            SaturatingAdd(
                _worldTicks.CurrentTick,
                delay.Value);

        if (request.Neighborhood)
        {
            _fluidUpdates.ScheduleNeighborhood(
                request.Fluid,
                request.Position,
                dueTick);
        }
        else
        {
            _fluidUpdates.ScheduleAt(
                new FluidTickKey(
                    request.Fluid,
                    request.Position),
                dueTick);
        }
    }

    private void TryStartFluidMeshWorker()
    {
        if (_fluidMeshWorker.IsRunning ||
            !_fluidMeshUpdates.HasWork)
        {
            return;
        }

        var drained =
            _fluidMeshUpdates.Drain();

        if (drained.IsEmpty)
        {
            return;
        }

        var filtered =
            drained.DirtyMeshlets
                .Where(entry =>
                    _chunkPresentations.Contains(entry.Key) &&
                    _world.ContainsChunk(entry.Key))
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value);

        if (filtered.Count == 0)
        {
            return;
        }

        _fluidMeshWorker.TryStart(
            _world,
            _blocks,
            _fluids,
            new WorldMeshBatch(filtered),
            _fluidContentRevisions);
    }

    private void PollFluidMeshWorker()
    {
        if (!_fluidMeshWorker.TryTakeCompleted(
                out var result,
                out var error))
        {
            return;
        }

        if (error is not null)
        {
            GD.PushError(error.ToString());
            return;
        }

        if (result is null)
        {
            return;
        }

        if (!_world.IsContentStampCurrent(
                result.ContentStamp))
        {
            _fluidMeshUpdates.Requeue(
                result.SourceBatch);
            TryStartFluidMeshWorker();
            return;
        }

        var accepted = 0;
        var stale = 0;

        foreach (var meshlet in result.Meshlets)
        {
            if (!_world.ContainsChunk(
                    meshlet.Coord) ||
                !_chunkPresentations.Contains(
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

            if (_fluidContentRevisions.IsCurrent(
                    key,
                    revision))
            {
                _chunkPresentations.EnqueueFluidPublication(
                    meshlet,
                    revision,
                    result.ContentStamp);
                accepted++;
            }
            else
            {
                _fluidMeshUpdates.EnqueueMeshlets(
                    meshlet.Coord,
                    ChunkMeshletMask.Single(
                        meshlet.MeshletIndex));
                stale++;
            }
        }

        GD.Print(
            $"world.fluid_mesh worker_ms=" +
            $"{result.WorkerMilliseconds:F2} " +
            $"accepted={accepted} stale={stale}");

        TryStartFluidMeshWorker();
    }

    private void IntegrateFluidMeshletPublications()
    {
        var stats =
            _chunkPresentations.IntegrateFluidPublications(
                MaxFluidMeshletPublishesPerFrame,
                _worldFrameBudget);

        if (stats.Handled > 0)
        {
            GD.Print(
                $"world.fluid_meshlets published={stats.Published} " +
                $"stale={stats.Stale} " +
                $"remaining={stats.Remaining}");
        }
    }

    private void TryStartTerrainMeshWorker()
    {
        if (_terrainMeshWorker.IsRunning ||
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
                    _chunkPresentations.Contains(entry.Key) &&
                    _world.ContainsChunk(entry.Key))
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value);

        if (filtered.Count == 0)
        {
            return;
        }

        _terrainMeshWorker.TryStart(
            _world,
            _blocks,
            _terrainTextureLookup,
            new WorldMeshBatch(filtered),
            _contentRevisions);
    }

    private void PollTerrainMeshWorker()
    {
        if (!_terrainMeshWorker.TryTakeCompleted(
                out var result,
                out var error))
        {
            return;
        }

        if (error is not null)
        {
            GD.PushError(error.ToString());
            return;
        }

        if (result is null)
        {
            return;
        }

        if (!_world.IsContentStampCurrent(
                result.ContentStamp))
        {
            _worldUpdates.RequeueMeshlets(
                result.SourceBatch);
            TryStartTerrainMeshWorker();
            return;
        }

        var accepted = 0;
        var stale = 0;

        foreach (var meshlet in result.Meshlets)
        {
            if (!_world.ContainsChunk(meshlet.Coord) ||
                !_chunkPresentations.Contains(meshlet.Coord))
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
                _chunkPresentations.EnqueueTerrainPublication(
                    meshlet,
                    revision,
                    result.ContentStamp);
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

        TryStartTerrainMeshWorker();
    }

    private void TryStartLightingWorker()
    {
        if (_lightingWorker.IsRunning ||
            !_worldUpdates.HasLightingWork)
        {
            return;
        }

        var batch =
            _worldUpdates.DrainLighting();

        if (batch.IsEmpty)
        {
            return;
        }

        _lightingWorker.TryStart(
            _world,
            _blocks,
            _fluids,
            batch);
    }

    private void PollLightingWorker()
    {
        if (!_lightingWorker.TryTakeCompleted(
                out var result,
                out var error))
        {
            return;
        }

        if (error is not null)
        {
            GD.PushError(error.ToString());
            return;
        }

        if (result is null)
        {
            return;
        }

        if (!result.Dependencies.IsCurrent(
                _world))
        {
            _worldUpdates.RequeueLighting(
                result.SourceBatch);
            TryStartLightingWorker();
            return;
        }

        var integration =
            _lightingIntegration.Apply(
                result.LightingSnapshot,
                result.Lighting.ChangedPositions);

        GD.Print(
            $"world.lighting worker_ms=" +
            $"{result.WorkerMilliseconds:F2} " +
            $"light_changes=" +
            $"{integration.ChangedVoxelCount} " +
            $"dirty_chunks=" +
            $"{integration.DirtyChunkCount} " +
            $"dirty_meshlets=" +
            $"{integration.DirtyMeshletCount} " +
            $"light_processed=" +
            $"{result.Lighting.ProcessedVoxelCount}");

        TryStartTerrainMeshWorker();
        TryStartLightingWorker();
    }

    private void IntegrateMeshletPublications()
    {
        var stats =
            _chunkPresentations.IntegrateTerrainPublications(
                MaxMeshletPublishesPerFrame,
                _worldFrameBudget);

        if (stats.Handled > 0)
        {
            GD.Print(
                $"world.meshlets published={stats.Published} " +
                $"stale={stats.Stale} " +
                $"remaining={stats.Remaining} " +
                $"publish_ms={stats.ElapsedMilliseconds:F2}");
        }
    }

    private static ulong SaturatingAdd(
        ulong value,
        ulong amount) =>
        ulong.MaxValue - value < amount
            ? ulong.MaxValue
            : value + amount;

    private void BeginWorldFrameBudget(double delta)
    {
        _worldFrameBudget =
            WorldFrameWorkBudget.Begin(
                delta,
                Stopwatch.GetTimestamp(),
                Stopwatch.Frequency);
    }

    private bool WorldBudgetExhausted() =>
        _worldFrameBudget.Exhausted(
            Stopwatch.GetTimestamp());


}
