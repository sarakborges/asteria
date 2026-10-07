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
    private static readonly int MaxMaterializationTasksInFlight =
        Math.Clamp(
            (System.Environment.ProcessorCount - 2) / 2,
            1,
            2);
    private static readonly int MaxMaterializationDispatchesPerFrame =
        MaxMaterializationTasksInFlight;
    private const int MaxMaterializationResultsPerFrame = 8;
    private const int MaxPresentationPublicationsPerFrame = 4;
    private const int MaxMeshletPublishesPerFrame = 4;
    private const int MaxInteractiveTerrainMeshletsPerWorker = 8;
    private const int MaxTerrainMeshletsPerWorker = 16;
    private const int MaxFluidMeshletsPerWorker = 16;
    private const int MaxFluidMeshletPublishesPerFrame = 4;
    private const int MaxFluidUpdatesPerWorker = 512;
    private const int MaxChunkEvictionsPerFrame = 2;

    private WorldUpdateQueue _worldUpdates =>
        _sessions.Active.WorldUpdates;
    private WorldTickClock _worldTicks =>
        _sessions.Active.WorldTicks;
    private FluidUpdateQueue _fluidUpdates =>
        _sessions.Active.FluidUpdates;
    private FluidMeshUpdateQueue _fluidMeshUpdates =>
        _sessions.Active.FluidMeshUpdates;
    private BlockPhysicsUpdateQueue _blockPhysicsUpdates =>
        _sessions.Active.BlockPhysicsUpdates;
    private MeshletContentRevisions _contentRevisions =>
        _sessions.Active.ContentRevisions;
    private MeshletContentRevisions _fluidContentRevisions =>
        _sessions.Active.FluidContentRevisions;
    private ChunkResidencyRuntime _residency =>
        _sessions.Active.Residency;
    private ChunkPresentationController _chunkPresentations =>
        _sessions.Active.Presentations;
    private ChunkStreamingController _chunkStreaming =>
        _sessions.Active.Streaming;

    private PackSelection _packSelection =
        PackSelection.Default;
    private JsonElement _uiTheme;

    private VoxelWorld _world =>
        _sessions.Active.World;
    private VoxelMutationRuntime _mutations =>
        _sessions.Active.Mutations;
    private BlockInteractionRuntime _blockInteractions =>
        _sessions.Active.BlockInteractions;
    private BlockEntityFrameController _blockEntities =>
        _sessions.Active.BlockEntities;
    private UnderwaterViewPresentation? _underwaterView;

    private TerrainMeshPipeline _terrainMeshPipeline =>
        _sessions.Active.TerrainMesh;
    private FluidMeshPipeline _fluidMeshPipeline =>
        _sessions.Active.FluidMesh;
    private FluidSimulationRuntime _fluidSimulationRuntime =>
        _sessions.Active.FluidSimulation;
    private LightingRuntime _lightingRuntime =>
        _sessions.Active.Lighting;

    private BlockRegistry _blocks = null!;
    private FluidRegistry _fluids = null!;
    private BiomeRegistry _biomes = null!;
    private DimensionRegistry _dimensions = null!;
    private DimensionSessionStateStore _sessionStates = null!;
    private DimensionSessionController _sessions = null!;
    private DimensionDefinition _dimension =>
        _sessions.Active.Dimension;
    private ulong _dimensionSeed =>
        _sessions.Active.DimensionSeed;
    private ChunkCoord _spawnChunk =>
        _sessions.Active.InitialStreamingCenter;
    private DimensionEnvironmentPresentation _dimensionEnvironment = null!;
    private FpsPlayer? _player;
    private Node _webUi = null!;
    private TerrainTextureCatalog _terrainTextures = null!;
    private TerrainTextureLookup _terrainTextureLookup = null!;
    private VoxelTerrainMaterialSet _terrainMaterials = null!;
    private FluidMaterialCatalog _fluidMaterials = null!;
    private BlockRuntimeId _placementBlock;
    private readonly WorldHudStateTracker _worldHud =
        new();

    private WorldFrameWorkBudget _worldFrameBudget;
    private bool _worldReadySent;
    private bool _debugHudVisible;
    private ulong? _worldSeed;
    private ulong _suggestedWorldSeed;

    [Export]
    public string StartupDimensionId { get; set; } =
        "asteria:overworld";

    public override void _Ready()
    {
        _uiTheme =
            UiThemeLoader.LoadProjectTheme(
                _packSelection);
        SetupWebUi();

        _blocks =
            BlockContentLoader.LoadProjectBlocks(
                _packSelection);
        _fluids =
            FluidContentLoader.LoadProjectFluids(
                _packSelection);
        _biomes =
            BiomeContentLoader.LoadProjectBiomes(
                _packSelection);
        _dimensions =
            DimensionContentLoader.LoadProjectDimensions(
                _packSelection);
        _dimensions.ValidateBiomes(
            _biomes);
        _dimensions.ValidateBlocks(
            _blocks);
        _dimensions.ValidateFluids(
            _fluids);

        _terrainTextures =
            TerrainTextureCatalog.Create(
                _blocks,
                _packSelection);
        _terrainTextureLookup =
            _terrainTextures.CreateLookup();
        _terrainMaterials =
            VoxelTerrainMaterialSet.Create(
                _terrainTextures);
        _fluidMaterials =
            FluidMaterialCatalog.Create(
                _fluids,
                _packSelection);

        _dimensionEnvironment =
            new DimensionEnvironmentPresentation(
                this);
        _suggestedWorldSeed =
            WorldCreationSeed.GenerateRandom();
        _webUi.Call(
            "set_creation_mode",
            true);

        GD.Print(
            $"pack: {_packSelection.Name}");
        GD.Print(
            $"block content: loaded {_blocks.AuthoredCount} definitions, " +
            $"{_terrainTextures.TextureCount} terrain textures");
        GD.Print(
            $"fluid content: loaded {_fluids.AuthoredCount} definitions");
        GD.Print(
            $"biome content: loaded {_biomes.Count} definitions");
        GD.Print(
            $"dimension content: loaded {_dimensions.Count} definitions; " +
            "waiting for world creation");
    }

    public override void _Input(InputEvent @event)
    {
        if (_worldSeed is null)
        {
            return;
        }

        if (@event is not InputEventKey keyEvent ||
            !keyEvent.Pressed ||
            keyEvent.Echo)
        {
            return;
        }

        switch (keyEvent.Keycode)
        {
            case Key.F3:
                _debugHudVisible =
                    !_debugHudVisible;
                SendDebugHudState();
                GetViewport().SetInputAsHandled();
                break;

            case Key.F4:
                if (TryCycleDimensionForQa())
                {
                    GetViewport().SetInputAsHandled();
                }

                break;
        }
    }

    public override void _Process(double delta)
    {
        if (_worldSeed is null)
        {
            return;
        }

        BeginWorldFrameBudget(delta);

        if (_sessions.IsTransitioning)
        {
            AdvanceDimensionTransition();
            return;
        }

        _worldTicks.Advance(
            delta,
            WorldTicksPerSecond);

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
                _dimension.GravityStrength));

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

        SendWorldHudState();

        if (!_worldReadySent &&
            _chunkPresentations.IsFullyPublished(
                _spawnChunk))
        {
            _worldReadySent = true;
            SetupPlayer();
            SendWorldReady();
        }
    }

    public bool TransitionToDimension(
        string dimensionId)
    {
        if (_player is null ||
            _sessions.IsTransitioning)
        {
            return false;
        }

        var position =
            _player.GlobalPosition;
        return BeginDimensionTransition(
            new DimensionId(
                dimensionId),
            new NVector3(
                position.X,
                position.Y,
                position.Z));
    }

    private bool BeginDimensionTransition(
        DimensionId target,
        NVector3 destination)
    {
        if (_player is null)
        {
            return false;
        }

        var position =
            _player.GlobalPosition;
        var source =
            new NVector3(
                position.X,
                position.Y,
                position.Z);

        if (!_sessions.RequestTransition(
                target,
                source,
                destination))
        {
            return false;
        }

        RetirePlayerForDimensionTransition();
        _worldReadySent = false;

        SendWebUi(
            "game.dimension_transition",
            new
            {
                from =
                    _dimension.Id.Value,
                to =
                    target.Value,
            });

        GD.Print(
            $"dimension.transition begin from={_dimension.Id} to={target}");
        return true;
    }

    private void AdvanceDimensionTransition()
    {
        var completion =
            _sessions.AdvanceTransition(
                _worldFrameBudget,
                ReportDimensionRetirementDrain);

        if (completion is null)
        {
            return;
        }

        ActivateCurrentDimensionPresentation();

        ReportStreamingSelection(
            _chunkStreaming.SyncSelection(
                CurrentStreamingCenter()));

        SendWebUi(
            "game.dimension_changed",
            new
            {
                from =
                    completion.From.Value,
                to =
                    completion.To.Value,
                dimensionSeed =
                    _dimensionSeed,
                gravityStrength =
                    _dimension.GravityStrength,
            });

        GD.Print(
            $"dimension.transition complete from={completion.From} " +
            $"to={completion.To} archived_dirty={completion.Archive.ArchivedDirty} " +
            $"dropped_pristine={completion.Archive.DroppedPristine}");
    }

    private void ActivateCurrentDimensionPresentation()
    {
        _dimensionEnvironment.Apply(
            _dimension);
    }

    private DimensionRuntimeSession CreateDimensionSession(
        DimensionSessionState state) =>
        new(
            this,
            state,
            _blocks,
            _fluids,
            _biomes,
            _terrainTextureLookup,
            _terrainMaterials,
            _fluidMaterials,
            new DimensionRuntimeSessionSettings(
                WorldTicksPerSecond,
                RenderDistanceChunks,
                RetentionMarginChunks,
                MaxMaterializationTasksInFlight,
                MaxMaterializationDispatchesPerFrame,
                MaxMaterializationResultsPerFrame,
                MaxPresentationPublicationsPerFrame,
                MaxInteractiveTerrainMeshletsPerWorker,
                MaxTerrainMeshletsPerWorker,
                MaxFluidMeshletsPerWorker,
                MaxFluidUpdatesPerWorker,
                MaxChunkEvictionsPerFrame));

    private bool TryCycleDimensionForQa()
    {
        if (_player is null ||
            _sessions.IsTransitioning)
        {
            return false;
        }

        var definitions =
            _dimensions
                .Definitions()
                .ToArray();

        if (definitions.Length < 2)
        {
            return false;
        }

        var currentIndex =
            Array.FindIndex(
                definitions,
                definition =>
                    definition.Id ==
                    _dimension.Id);
        var next =
            definitions[
                (currentIndex + 1) %
                definitions.Length];
        var position =
            _player.GlobalPosition;

        return BeginDimensionTransition(
            next.Id,
            new NVector3(
                position.X,
                position.Y,
                position.Z));
    }

    private void RetirePlayerForDimensionTransition()
    {
        if (_player is null)
        {
            return;
        }

        _player.BreakRequested -=
            BreakTargetBlock;
        _player.PlaceRequested -=
            PlaceTargetBlock;
        _player.MouseCaptureChanged -=
            SendMouseCaptureState;
        _player.FluidContactChanged -=
            OnPlayerFluidContactChanged;
        _player.QueueFree();
        _player = null;
        _underwaterView = null;
        _worldHud.Reset();

        Input.MouseMode =
            Input.MouseModeEnum.Visible;
        SendMouseCaptureState(
            captured: false);
    }

    private void ReportDimensionRetirementDrain(
        DimensionRetirementDrainReport report)
    {
        ReportResidencyUpdate(
            report.Materialization);

        foreach (var error in
                 report.WorkerErrors)
        {
            GD.PushError(
                $"Dimension retirement worker failed:\n{error}");
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

            if (_debugHudVisible)
            {
                GD.Print(
                    $"webui -> godot: {type}");
            }

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
                case "ui.world.randomize":
                    if (_worldSeed is null)
                    {
                        _suggestedWorldSeed =
                            WorldCreationSeed.GenerateRandom();
                        SendWorldCreationState();
                    }

                    break;
                case "ui.world.create":
                    if (_worldSeed is null)
                    {
                        StartRequestedWorld(
                            document.RootElement);
                    }

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
        SendDebugHudState();

        if (_worldSeed is null)
        {
            SendWorldCreationState();
            return;
        }

        SendHotbarState();
        SendWorldHudState(
            force: true);

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

    private void SendWorldCreationState()
    {
        SendWebUi(
            "game.world_creation",
            new
            {
                seed =
                    WorldCreationSeed.Format(
                        _suggestedWorldSeed),
            });
    }

    private void StartRequestedWorld(
        JsonElement message)
    {
        if (!message.TryGetProperty(
                "payload",
                out var payload) ||
            payload.ValueKind !=
                JsonValueKind.Object ||
            !payload.TryGetProperty(
                "seed",
                out var seedValue) ||
            seedValue.ValueKind !=
                JsonValueKind.String)
        {
            SendWorldCreationError(
                "Seed must be a decimal string.");
            return;
        }

        var rawSeed =
            seedValue.GetString();
        var validSeed =
            string.IsNullOrWhiteSpace(
                rawSeed)
                ? _suggestedWorldSeed
                : WorldCreationSeed.TryParse(
                    rawSeed,
                    out var manualSeed)
                    ? manualSeed
                    : (ulong?)null;

        if (validSeed is not
            { } selectedSeed)
        {
            SendWorldCreationError(
                "Seed must be an unsigned 64-bit decimal number (0–18446744073709551615).");
            return;
        }

        StartWorld(
            selectedSeed);
    }

    private void StartWorld(
        ulong worldSeed)
    {
        if (_worldSeed is not null)
        {
            return;
        }

        _sessionStates =
            new DimensionSessionStateStore(
                worldSeed,
                _dimensions);
        _sessions =
            new DimensionSessionController(
                _sessionStates,
                CreateDimensionSession);
        _sessions.Start(
            new DimensionId(
                StartupDimensionId));
        ActivateCurrentDimensionPresentation();

        _placementBlock =
            _blocks.GetId(
                TestChunkFactory.StoneId);
        _worldSeed =
            worldSeed;

        _webUi.Call(
            "set_creation_mode",
            false);
        SendWebUi(
            "game.world_creation.started",
            new
            {
                seed =
                    WorldCreationSeed.Format(
                        worldSeed),
            });
        SendHotbarState();

        GD.Print(
            $"world.start seed={worldSeed} " +
            $"dimension={_dimension.Id} " +
            $"dimension_seed={_dimensionSeed}");
        GD.Print(
            $"streaming: render_distance={RenderDistanceChunks} " +
            $"retention_margin={RetentionMarginChunks} " +
            $"materialization_in_flight={MaxMaterializationTasksInFlight}");

        ReportStreamingSelection(
            _chunkStreaming.SyncSelection(
                CurrentStreamingCenter()));
    }

    private void SendWorldCreationError(
        string message)
    {
        SendWebUi(
            "game.world_creation.error",
            new { message });
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
                biomes = _biomes.Count,
                dimensions = _dimensions.Count,
                worldSeed =
                    WorldCreationSeed.Format(
                        _worldSeed!.Value),
                dimensionSeed = _dimensionSeed,
                dimension = _dimension.Id.Value,
                gravityStrength = _dimension.GravityStrength,
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

    private void SendDebugHudState()
    {
        SendWebUi(
            "game.hud.debug",
            new
            {
                visible =
                    _debugHudVisible,
            });
    }

    private void SendWorldHudState(
        bool force = false)
    {
        if (_player is null)
        {
            return;
        }

        if (force)
        {
            _worldHud.Reset();
        }

        if (!_worldHud.TryCapture(
                _player,
                _dimension.Id,
                _sessions.Active.Generator.Biomes,
                out var state))
        {
            return;
        }

        SendWebUi(
            "game.hud.world",
            new
            {
                sphere =
                    state.Sphere,
                biome =
                    state.Biome,
                x =
                    state.X,
                y =
                    state.Y,
                z =
                    state.Z,
                heading =
                    state.HeadingDegrees,
            });
    }

    private void SendHotbarState()
    {
        if (_placementBlock.IsAir)
        {
            return;
        }

        var definition =
            _blocks.GetDefinition(
                _placementBlock);

        SendWebUi(
            "game.hud.hotbar",
            new
            {
                selectedIndex = 0,
                slots = new[]
                {
                    new
                    {
                        id = definition.Id,
                        quantity = 1,
                    },
                },
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

        var initialPosition =
            _sessions.Active.InitialPlayerPosition;

        _player = new FpsPlayer
        {
            Name = "Player",
            Position =
                new Vector3(
                    initialPosition.X,
                    initialPosition.Y,
                    initialPosition.Z),
            GravityStrength =
                _dimension.GravityStrength,
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
        SendWorldHudState(
            force: true);

        GD.Print(
            $"streaming: dimension={_dimension.Id} presentation ready; player activated");
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
            return _spawnChunk;
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

    private void ReportStreamingSelection(
        ChunkStreamingSelectionReport selection)
    {
        if (!_debugHudVisible ||
            !selection.Changed)
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

        if (!_debugHudVisible)
        {
            return;
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
        if (!_debugHudVisible ||
            reserved <= 0)
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
        if (!_debugHudVisible)
        {
            return;
        }

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
        SendHotbarState();
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

    private void ReportBlockEntityFrame(
        BlockEntityFrameReport report)
    {
        if (!_debugHudVisible ||
            !report.HasChanges)
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

        if (_debugHudVisible)
        {
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
        }

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

        if (_debugHudVisible)
        {
            GD.Print(
                $"world.fluid_mesh worker_ms=" +
                $"{completed.WorkerMilliseconds:F2} " +
                $"accepted={completed.Accepted} " +
                $"stale={completed.Stale}");
        }
    }

    private void IntegrateFluidMeshletPublications()
    {
        var stats =
            _fluidMeshPipeline.IntegratePublications(
                MaxFluidMeshletPublishesPerFrame,
                _worldFrameBudget);

        if (_debugHudVisible &&
            stats.Handled > 0)
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

        if (_debugHudVisible)
        {
            GD.Print(
                $"world.geometry worker_ms=" +
                $"{completed.WorkerMilliseconds:F2} " +
                $"accepted={completed.Accepted} " +
                $"stale={completed.Stale}");
        }
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

        if (_debugHudVisible)
        {
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
        }

        TryStartTerrainMeshWorker();
    }

    private void IntegrateMeshletPublications()
    {
        var stats =
            _terrainMeshPipeline.IntegratePublications(
                MaxMeshletPublishesPerFrame,
                _worldFrameBudget);

        if (_debugHudVisible &&
            stats.Handled > 0)
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
