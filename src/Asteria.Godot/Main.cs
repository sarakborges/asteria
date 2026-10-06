using System.Diagnostics;
using System.Text.Json;
using Asteria.Client.Content;
using Asteria.Client.Gameplay;
using Asteria.Client.Rendering;
using Asteria.Core.Content;
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
    private const int MaxInteractiveTerrainMeshletsPerWorker = 8;
    private const int MaxTerrainMeshletsPerWorker = 16;
    private const int MaxFluidMeshletsPerWorker = 16;
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
    private ChunkStreamingController _chunkStreaming = null!;

    private PackSelection _packSelection =
        PackSelection.Default;
    private JsonElement _uiTheme;

    private readonly VoxelWorld _world = new();
    private VoxelMutationRuntime _mutations = null!;
    private BlockInteractionRuntime _blockInteractions = null!;
    private BlockEntityFrameController _blockEntities = null!;
    private UnderwaterViewPresentation? _underwaterView;

    private TerrainMeshPipeline _terrainMeshPipeline = null!;
    private FluidMeshPipeline _fluidMeshPipeline = null!;
    private FluidSimulationRuntime _fluidSimulationRuntime = null!;
    private LightingRuntime _lightingRuntime = null!;

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
        _uiTheme =
            UiThemeLoader.LoadProjectTheme(
                _packSelection);
        SetupWebUi();

        _blocks = BlockContentLoader.LoadProjectBlocks(_packSelection);
        _fluids = FluidContentLoader.LoadProjectFluids(_packSelection);
        var droppedBlocks =
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
        var lightingIntegration =
            new LightingResultIntegrator(
                _world,
                _worldUpdates,
                _fluidMeshUpdates);
        _fluidSimulationRuntime =
            new FluidSimulationRuntime(
                _world,
                _fluids,
                _fluidUpdates,
                _mutations,
                _worldTicks,
                WorldTicksPerSecond,
                MaxFluidUpdatesPerWorker);
        _lightingRuntime =
            new LightingRuntime(
                _world,
                _blocks,
                _fluids,
                _worldUpdates,
                lightingIntegration);
        var blockPhysics =
            new BlockPhysicsRuntime(
                _world,
                _blocks,
                _mutations,
                _blockPhysicsUpdates,
                droppedBlocks);
        _blockInteractions =
            new BlockInteractionRuntime(
                _world,
                _blocks,
                _mutations,
                droppedBlocks);
        _terrainTextures = TerrainTextureCatalog.Create(_blocks, _packSelection);
        _terrainTextureLookup = _terrainTextures.CreateLookup();
        _terrainMaterials = VoxelTerrainMaterialSet.Create(_terrainTextures);
        _fluidMaterials = FluidMaterialCatalog.Create(_fluids);
        var blockEntityPresentations =
            new BlockEntityPresentationController(
                this,
                _blocks,
                _fluids,
                _terrainTextureLookup,
                _terrainMaterials);
        _blockEntities =
            new BlockEntityFrameController(
                _worldTicks,
                blockPhysics,
                droppedBlocks,
                _blockPhysicsUpdates,
                blockEntityPresentations);
        _residency =
            new ChunkResidencyRuntime(
                _world,
                _blocks,
                _fluids,
                _worldUpdates,
                _fluidUpdates,
                _fluidMeshUpdates,
                blockPhysics,
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
        _terrainMeshPipeline =
            new TerrainMeshPipeline(
                _world,
                _blocks,
                _terrainTextureLookup,
                _worldUpdates,
                _contentRevisions,
                _chunkPresentations,
                MaxInteractiveTerrainMeshletsPerWorker,
                MaxTerrainMeshletsPerWorker);
        _fluidMeshPipeline =
            new FluidMeshPipeline(
                _world,
                _blocks,
                _fluids,
                _fluidMeshUpdates,
                _fluidContentRevisions,
                _chunkPresentations,
                MaxFluidMeshletsPerWorker);
        _chunkStreaming =
            new ChunkStreamingController(
                _residency,
                _chunkPresentations,
                new ChunkStreamingControllerSettings(
                    RenderDistanceChunks,
                    RetentionMarginChunks,
                    minimumChunkY: 0,
                    maximumChunkY: 1,
                    MaxPresentationPublicationsPerFrame));
        _placementBlock =
            _blocks.GetId(TestChunkFactory.StoneId);

        GD.Print(
            $"pack: {_packSelection.Name}");
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
        ReportStreamingSelection(
            _chunkStreaming.SyncSelection(
                CurrentStreamingCenter()));
    }

    public override void _Process(double delta)
    {
        _worldTicks.Advance(
            delta,
            WorldTicksPerSecond);
        BeginWorldFrameBudget(delta);

        var streamingBegin =
            _chunkStreaming.BeginFrame(
                CurrentStreamingCenter(),
                _worldFrameBudget);
        ReportStreamingSelection(
            streamingBegin.Selection);
        ReportResidencyUpdate(
            streamingBegin.Collected);
        ReportResidencyUpdate(
            streamingBegin.Dispatched);
        ReportPresentationReservations(
            streamingBegin.ReservedPresentations);

        ReportBlockEntityFrame(
            _blockEntities.Advance(
                delta,
                WorldGravityStrength));

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

        var streamingEnd =
            _chunkStreaming.EndFrame(
                _worldFrameBudget);
        ReportRetirements(
            streamingEnd.Retirements);

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
            "game.ui_theme",
            _uiTheme);
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
                fallingBlocks = _blockEntities.FallingCount,
                droppedBlocks = _blockEntities.DroppedCount,
                physicsUpdates = _blockEntities.PendingPhysicsUpdates,
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
        _player.FluidMotionProvider =
            fluid =>
                _fluids
                    .GetDefinition(fluid)
                    .Motion;
        _player.BreakRequested += BreakTargetBlock;
        _player.PlaceRequested += PlaceTargetBlock;
        _player.MouseCaptureChanged +=
            SendMouseCaptureState;
        _player.FluidContactChanged +=
            OnPlayerFluidContactChanged;
        AddChild(_player);

        _underwaterView =
            new UnderwaterViewPresentation(
                _player.Camera,
                _fluids);

        SendWebUi(
            "game.player_ready",
            new { controller = "fps" });

        GD.Print(
            "streaming: origin presentation ready; player activated");
    }

    private void OnPlayerFluidContactChanged(
        FluidBodyContact contact)
    {
        _underwaterView?.Apply(
            contact);
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

    private static void ReportStreamingSelection(
        ChunkStreamingSelectionReport selection)
    {
        if (!selection.Changed)
        {
            return;
        }

        GD.Print(
            $"streaming.selection center={selection.Center} " +
            $"desired={selection.DesiredCount} " +
            $"retained={selection.RetainedCount} " +
            $"pending={selection.PendingCount} " +
            $"movement={selection.MovementDirection}");
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

    private void ReportPresentationReservations(
        int reserved)
    {
        if (reserved <= 0)
        {
            return;
        }

        GD.Print(
            $"chunk.presentation.reserve count={reserved} " +
            $"presented={_chunkPresentations.Count}");
    }

    private void ReportRetirements(
        IReadOnlyList<ChunkResidencyRetirement> retirements)
    {
        foreach (var retirement in retirements)
        {
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

    private static void ReportBlockEntityFrame(
        BlockEntityFrameReport report)
    {
        if (!report.HasChanges)
        {
            return;
        }

        GD.Print(
            $"world.block_physics falling_started=" +
            $"{report.Wake.FallingStarted} " +
            $"unsupported_removed=" +
            $"{report.Wake.UnsupportedRemoved} " +
            $"landed={report.Landed} " +
            $"falling_active={report.FallingCount} " +
            $"drops_active={report.DroppedCount} " +
            $"drops_settled={report.Dropped.Settled} " +
            $"drops_expired={report.Dropped.Expired} " +
            $"queued={report.PendingPhysicsUpdates}");
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
        _fluidSimulationRuntime.TryStartReadyWork();
    }

    private void PollFluidWorker()
    {
        if (!_fluidSimulationRuntime.TryPollCompleted(
                out var report,
                out var error))
        {
            return;
        }

        if (error is not null)
        {
            GD.PushError(error.ToString());
            return;
        }

        if (report is null)
        {
            return;
        }

        if (report.Kind !=
            FluidSimulationCompletionKind.Applied)
        {
            return;
        }

        GD.Print(
            $"world.fluid tick={report.Tick} " +
            $"worker_ms={report.WorkerMilliseconds:F2} " +
            $"processed={report.ProcessedVoxelCount} " +
            $"changes={report.AppliedChangeCount} " +
            $"changed_voxels={report.UniquePositionCount} " +
            $"scheduled={report.ScheduledRequestCount} " +
            $"downhill_searches={report.DownhillSearchCount} " +
            $"downhill_nodes={report.DownhillVisitedNodeCount} " +
            $"backlog={report.BacklogCount}");

        TryStartFluidMeshWorker();
    }

    private void TryStartFluidMeshWorker()
    {
        _fluidMeshPipeline.TryStartReadyWork();
    }

    private void PollFluidMeshWorker()
    {
        if (!_fluidMeshPipeline.TryPollCompleted(
                out var report,
                out var error))
        {
            return;
        }

        if (error is not null)
        {
            GD.PushError(error.ToString());
            return;
        }

        if (report is not { } completed ||
            completed.Kind !=
                MeshPipelineCompletionKind.Applied)
        {
            return;
        }

        GD.Print(
            $"world.fluid_mesh worker_ms=" +
            $"{completed.WorkerMilliseconds:F2} " +
            $"accepted={completed.Accepted} " +
            $"stale={completed.Stale}");
    }

    private void IntegrateFluidMeshletPublications()
    {
        var stats =
            _fluidMeshPipeline.IntegratePublications(
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
        _terrainMeshPipeline.TryStartReadyWork();
    }

    private void PollTerrainMeshWorker()
    {
        if (!_terrainMeshPipeline.TryPollCompleted(
                out var report,
                out var error))
        {
            return;
        }

        if (error is not null)
        {
            GD.PushError(error.ToString());
            return;
        }

        if (report is not { } completed ||
            completed.Kind !=
                MeshPipelineCompletionKind.Applied)
        {
            return;
        }

        GD.Print(
            $"world.geometry worker_ms=" +
            $"{completed.WorkerMilliseconds:F2} " +
            $"accepted={completed.Accepted} " +
            $"stale={completed.Stale}");
    }

    private void TryStartLightingWorker()
    {
        _lightingRuntime.TryStartReadyWork();
    }

    private void PollLightingWorker()
    {
        if (!_lightingRuntime.TryPollCompleted(
                out var report,
                out var error))
        {
            return;
        }

        if (error is not null)
        {
            GD.PushError(error.ToString());
            return;
        }

        if (report is null ||
            report.Kind !=
                LightingCompletionKind.Applied)
        {
            return;
        }

        GD.Print(
            $"world.lighting worker_ms=" +
            $"{report.WorkerMilliseconds:F2} " +
            $"light_changes=" +
            $"{report.ChangedVoxelCount} " +
            $"dirty_chunks=" +
            $"{report.DirtyChunkCount} " +
            $"dirty_meshlets=" +
            $"{report.DirtyMeshletCount} " +
            $"light_processed=" +
            $"{report.ProcessedVoxelCount}");

        TryStartTerrainMeshWorker();
    }

    private void IntegrateMeshletPublications()
    {
        var stats =
            _terrainMeshPipeline.IntegratePublications(
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

    private void BeginWorldFrameBudget(double delta)
    {
        _worldFrameBudget =
            WorldFrameWorkBudget.Begin(
                delta,
                Stopwatch.GetTimestamp(),
                Stopwatch.Frequency);
    }

}
