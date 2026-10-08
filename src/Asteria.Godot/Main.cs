using System.Diagnostics;
using System.Text.Json;
using Asteria.Client.Content;
using Asteria.Client.Gameplay;
using Asteria.Client.Rendering;
using Asteria.Client.Settings;
using Asteria.Core.Content;
using Asteria.Core.Settings;
using Asteria.Core.World;
using Godot;
using NVector3 = System.Numerics.Vector3;

namespace Asteria.Client;

public partial class Main : Node3D
{
    private const float InteractionDistance = 6f;
    private const int RetentionMarginChunks = 10;
    private static readonly int MaxMaterializationTasksInFlight =
        Math.Clamp(
            System.Environment.ProcessorCount - 2,
            1,
            4);
    private static readonly int MaxMaterializationDispatchesPerFrame =
        MaxMaterializationTasksInFlight;
    private const int MaxMaterializationResultsPerFrame = 8;
    private const int MaxPresentationPublicationsPerFrame = 4;
    private const int MaxMeshletPublishesPerFrame = 16;
    private const int MaxInteractiveTerrainMeshletsPerWorker = 8;
    private const int MaxTerrainMeshletsPerWorker = 16;
    private const int MaxFluidMeshletsPerWorker = 16;
    private const int MaxFluidMeshletPublishesPerFrame = 16;
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
    private ClientPreferencesController _clientSettings = null!;
    private WorldCatalogScanController _worldCatalog = null!;
    private KeybindCaptureController _keybindCapture = null!;

    private ClientPreferences _clientPreferences =>
        _clientSettings.Preferences;
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
    private AttachedLayerRegistry _layers = null!;
    private DyeRegistry _dyes = null!;
    private PackContentRegistry<ItemDefinition> _items = null!;
    private PackContentRegistry<ToolDefinition> _tools = null!;
    private InventoryContentCatalog _inventoryCatalog = null!;
    private InventoryDropIconCatalog _inventoryDropIcons = null!;
    private readonly Dictionary<string, string> _inventoryIconCache =
        new(StringComparer.Ordinal);
    private PackContentRegistry<CreatureDefinition> _creatures = null!;
    private PackContentRegistry<AttackDefinition> _attacks = null!;
    private (ulong Id, float Health, float Maximum)? _lastCreatureHud;
    private FluidRegistry _fluids = null!;
    private BiomeRegistry _biomes = null!;
    private StructureRegistry _structures = null!;
    private StructureSetRegistry _structureSets = null!;
    private DimensionRegistry _dimensions = null!;
    private DayNightCycleRegistry _dayNightCycles = null!;
    private AmbientParticleRegistry _ambientParticleDefinitions = null!;
    private AmbientParticleRuntime? _ambientParticleRuntime;
    private AmbientParticlePresentation? _ambientParticlePresentation;
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
    private readonly WorldHudStateTracker _worldHud =
        new();
    private readonly FpsHudStateTracker _fpsHud =
        new();
    private readonly TargetHudStateTracker _targetHud =
        new();
    private readonly WorldLoadingState _loading =
        new();

    private WorldLoadingProgress? _lastLoadingProgress;
    private (ulong Day, int Hour, int Minute)? _lastClockHud;
    private WorldFrameWorkBudget _worldFrameBudget;
    private bool _worldReadySent;
    private bool _inventoryOpen;
    private readonly PlayerChatSession _chat = new();
    private readonly ChatLocateController _chatLocate = new();
    private double _chatFeedbackSeconds;
    private bool _brushPaletteOpen;
    private int _publishedMiningStage = -1;
    private double _pickupAccumulator;
    private bool _debugHudVisible;
    private WorldDiagnosticsLog? _worldDiagnostics;
    private ulong? _worldSeed;
    private ulong _suggestedWorldSeed;

    [Export]
    public string StartupDimensionId { get; set; } =
        "asteria:overworld";

    public override void _Ready()
    {
        _worldDiagnostics = WorldDiagnosticsLog.Open();
        _clientSettings = new ClientPreferencesController(
            ClientPreferencesStore.FromUserDataDirectory());
        _worldCatalog = WorldCatalogScanController.FromUserDataDirectory();
        _keybindCapture = new KeybindCaptureController(
            _clientSettings);
        _uiTheme =
            UiThemeLoader.LoadProjectTheme(
                _packSelection);
        SetupWebUi();

        _blocks =
            BlockContentLoader.LoadProjectBlocks(
                _packSelection);
        _layers = AttachedLayerContentLoader.LoadProjectLayers(_packSelection);
        _dyes = DyeContentLoader.LoadProjectDyes(_packSelection);
        _fluids =
            FluidContentLoader.LoadProjectFluids(
                _packSelection);
        _items =
            ItemContentLoader.LoadProjectItems(
                _packSelection);
        _tools =
            ToolContentLoader.LoadProjectTools(
                _packSelection);
        _inventoryCatalog = new InventoryContentCatalog(
            _blocks, _items, _tools, _layers);
        _inventoryDropIcons = new InventoryDropIconCatalog(
            _packSelection, _inventoryCatalog);
        _creatures =
            CreatureContentLoader.LoadProjectCreatures(
                _packSelection);
        _attacks =
            AttackContentLoader.LoadProjectAttacks(
                _packSelection);
        _biomes =
            BiomeContentLoader.LoadProjectBiomes(
                _packSelection);
        _structures =
            StructureContentLoader.LoadProjectStructures(
                _packSelection);
        _structureSets =
            StructureSetContentLoader.LoadProjectStructureSets(
                _packSelection);
        _dimensions =
            DimensionContentLoader.LoadProjectDimensions(
                _packSelection);
        _dayNightCycles =
            DayNightCycleContentLoader.LoadProjectCycles(
                _packSelection);
        _dimensions.ValidateDayNightCycles(
            _dayNightCycles);
        _dimensions.ValidateBiomes(
            _biomes);
        _dimensions.ValidateBlocks(
            _blocks);
        _dimensions.ValidateFluids(
            _fluids);
        _ambientParticleDefinitions =
            AmbientParticleContentLoader.LoadProjectParticles(_packSelection);
        _ambientParticleDefinitions.ValidateReferences(_dimensions, _biomes, _fluids);
        _structures.ValidateBlocks(
            _blocks, _dyes, _layers);
        _structures.ValidateFluids(
            _fluids);
        _dimensions.ValidateStructures(
            _structures,
            _structureSets);

        _terrainTextures =
            TerrainTextureCatalog.Create(
                _blocks,
                _layers,
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
            $"item content: loaded {_items.Count} definitions");
        GD.Print(
            $"tool content: loaded {_tools.Count} definitions");
        GD.Print(
            $"creature content: loaded {_creatures.Count} definitions");
        GD.Print(
            $"biome content: loaded {_biomes.Count} definitions");
        GD.Print(
            $"structure content: loaded {_structures.Count} definitions");
        GD.Print(
            $"structure set content: loaded {_structureSets.Count} definitions");
        GD.Print(
            $"dimension content: loaded {_dimensions.Count} definitions; " +
            "waiting for world creation");
    }

    public override void _ExitTree()
    {
        _worldDiagnostics?.Dispose();
        _worldDiagnostics = null;
    }

    public override void _Input(InputEvent @event)
    {
        if (_keybindCapture.IsCapturing)
        {
            if (@event is InputEventKey captureKey &&
                captureKey.Pressed && !captureKey.Echo)
            {
                var action = _keybindCapture.PendingAction;
                var result = _keybindCapture.Press(captureKey);
                if (!_keybindCapture.IsCapturing)
                {
                    _player?.ResumeAfterKeyCapture();
                }

                SendWebUi(
                    "game.client_preferences.key_capture",
                    new
                    {
                        action = action?.ToString(),
                        status = result.ToString(),
                    });
                if (result is KeyCaptureResult.Changed or
                    KeyCaptureResult.SaveFailed)
                {
                    SendClientPreferences();
                }

                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_worldSeed is null)
        {
            return;
        }

        if (_chat.IsOpen)
        {
            if (@event is InputEventKey chatKey &&
                chatKey.Pressed && !chatKey.Echo &&
                chatKey.Keycode == Key.Escape)
            {
                CloseChat();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (_brushPaletteOpen &&
            @event is InputEventKey brushKey &&
            brushKey.Pressed && !brushKey.Echo &&
            brushKey.Keycode == Key.Escape)
        {
            CloseBrushPalette();
            GetViewport().SetInputAsHandled();
            return;
        }

        if (_inventoryOpen &&
            @event is InputEventKey modalKey &&
            modalKey.Pressed && !modalKey.Echo &&
            (modalKey.Keycode == Key.Escape ||
             GameplayKeyMap.Matches(
                 modalKey, _clientPreferences, KeybindAction.Inventory)))
        {
            CloseInventory();
            GetViewport().SetInputAsHandled();
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

            case Key.F5:
                if (_debugHudVisible && TrySpawnCreatureForQa())
                {
                    GetViewport().SetInputAsHandled();
                }

                break;
        }
    }

    public override void _Process(double delta)
    {
        PollWorldCatalog();
        PollChatLocate();
        AdvanceChatPresentation(delta);
        if (_worldSeed is null)
        {
            return;
        }

        BeginWorldFrameBudget(delta);
        AdvanceFpsHudState(delta);

        if (_sessions.IsTransitioning)
        {
            AdvanceDimensionTransition();
            return;
        }

        _sessions.Active.AdvanceWorldTime(
            delta);
        UpdateDayNightPresentation();

        var loadingWorld =
            _loading.Progress.Phase is
                WorldLoadingPhase.MaterializingInitialArea or
                WorldLoadingPhase.PreparingPresentation;
        var streamingCenter =
            loadingWorld
                ? _loading.Center
                : CurrentStreamingCenter();
        var streamingRadius =
            loadingWorld
                ? WorldLoadingState.InitialHorizontalRadiusChunks
                : _clientPreferences.RenderDistanceChunks;

        var streamingBegin =
            _chunkStreaming.BeginFrame(
                streamingCenter,
                _worldFrameBudget,
                streamingRadius);
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
        AdvanceItemPickup(delta);

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
        _worldDiagnostics?.FlushIfDue(
            _world.ChunkCount,
            _residency.PendingCount,
            _residency.MaterializingCount,
            _chunkPresentations.Count,
            _loading.Progress.Phase.ToString());

        if (loadingWorld)
        {
            AdvanceWorldLoading();
            return;
        }

        AdvanceSurvivalMining();
        _sessions.Active.MiningCracks.Sync(_world, _sessions.Active.Mining);

        if (_worldReadySent && _player is { } creatureObserver)
        {
            var position = creatureObserver.GlobalPosition;
            _sessions.Active.AdvanceCreatures(
                delta,
                new NVector3(position.X, position.Y, position.Z));
        }

        if (_worldReadySent && _player is { } particleObserver)
        {
            var cameraPosition = particleObserver.Camera.GlobalPosition;
            _ambientParticleRuntime!.Advance(
                (float)delta,
                new NVector3(cameraPosition.X, cameraPosition.Y, cameraPosition.Z),
                _dimension.Id,
                _dimension.Environment.Wind,
                _world,
                _sessions.Active.Generator,
                _fluids);
            _ambientParticlePresentation!.Sync(_ambientParticleRuntime);
        }

        SendWorldHudState();
        SendWorldClockState();
        SendTargetHudState();
        SyncArchitectsCompassPreview();
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
        _ambientParticleRuntime?.Clear();
        _ambientParticlePresentation?.Clear();
        _worldReadySent = false;
        _loading.BeginRetirement();
        _lastLoadingProgress = null;
        SendLoadingState();

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
        BeginWorldLoading();

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
            $"archived_pristine={completion.Archive.ArchivedPristine}");
    }

    private void ActivateCurrentDimensionPresentation()
    {
        _ambientParticleRuntime = new AmbientParticleRuntime(
            _ambientParticleDefinitions, _dimensionSeed);
        _ambientParticlePresentation = new AmbientParticlePresentation(
            _sessions.Active.Root, _ambientParticleDefinitions);
        _terrainMaterials.SetWind(_dimension.Environment.Wind);
        _dimensionEnvironment.Apply(
            _dimension,
            ActiveDayNightCycle(),
            _sessions.Active.DayNight);
        _lastClockHud = null;
        SendWorldClockState(force: true);
    }

    private DayNightCycleDefinition ActiveDayNightCycle() =>
        _dayNightCycles.Get(
            _dimension.DayNightCycleId ??
            throw new InvalidOperationException(
                $"Dimension {_dimension.Id} has no day-night cycle."));

    private void UpdateDayNightPresentation()
    {
        if (_worldTicks.TicksThisFrame != 0)
        {
            _dimensionEnvironment.Update(
                ActiveDayNightCycle(),
                _sessions.Active.DayNight);
        }

        if (_player is { } player)
        {
            _dimensionEnvironment.FollowCamera(
                player.Camera.GlobalPosition);
        }
    }

    private void SendWorldClockState(
        bool force = false)
    {
        var clock =
            _sessions.Active.DayNight;
        var (hour, minute) =
            clock.WorldTime;
        var state = (
            clock.Day,
            Hour: hour,
            Minute: minute);

        if (!force &&
            _lastClockHud == state)
        {
            return;
        }

        _lastClockHud = state;
        SendWebUi(
            "game.hud.clock",
            new
            {
                day = state.Day,
                hour = state.Hour,
                minute = state.Minute,
            });
    }

    private DimensionRuntimeSession CreateDimensionSession(
        DimensionSessionState state) =>
        new(
            this,
            state,
            _blocks,
            _layers,
            _dyes,
            _fluids,
            _biomes,
            _structures,
            _structureSets,
            _dayNightCycles,
            _creatures,
            _inventoryCatalog,
            _tools,
            _packSelection,
            _inventoryDropIcons.Resolve,
            _terrainTextureLookup,
            _terrainMaterials,
            _fluidMaterials,
            new DimensionRuntimeSessionSettings(
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

    private bool TrySpawnCreatureForQa()
    {
        if (!_worldReadySent || _inventoryOpen ||
            _sessions.IsTransitioning ||
            _player is not { IsMouseCaptured: true } player)
        {
            return false;
        }

        // QA-only explicit spawn; the generator owns safe destinations.
        var (_, target) = player.GetInteractionRay(5f);
        var safe = _sessions.Active.Generator.FindGeneratedDestination(
            Mathf.FloorToInt(target.X),
            Math.Max(0, Mathf.FloorToInt(target.Y)),
            Mathf.FloorToInt(target.Z),
            maxRadius: 12);

        if (safe is null ||
            !_world.IsLoadedAt(new WorldVoxelCoord(
                safe.Value.X, safe.Value.Y, safe.Value.Z)))
        {
            return false;
        }

        var feet = new NVector3(
            safe.Value.X + 0.5f,
            safe.Value.Y,
            safe.Value.Z + 0.5f);
        if (!_sessions.Active.TrySpawnCreature("asteria:slime_aqua", feet))
        {
            return false;
        }

        GD.Print($"creature.qa_spawn id=asteria:slime_aqua position={feet}");
        return true;
    }

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
        _player.ToolActionRequested -=
            RotateHeldBlock;
        _player.InventoryRequested -= OpenInventory;
        _player.ChatRequested -= OpenChat;
        _player.DropItemRequested -= DropSelectedItem;
        _player.HotbarSlotRequested -= SelectHotbar;
        _player.MouseCaptureChanged -=
            SendMouseCaptureState;
        _player.FlightStateChanged -=
            SendPlayerModeState;
        _player.FluidContactChanged -=
            OnPlayerFluidContactChanged;
        _sessionStates.Player.CancelDoubleTap();
        if (_chat.Close())
            SendChatState();
        if (_inventoryOpen)
        {
            _sessionStates.Player.Inventory.TryReturnCursor();
            _inventoryOpen = false;
            SendInventoryState();
        }
        if (_brushPaletteOpen)
        {
            _brushPaletteOpen = false;
            SendBrushPalette();
        }
        _sessions.Active.Mining.Cancel();
        PublishMiningProgress();
        _player.QueueFree();
        _player = null;
        _underwaterView = null;
        _worldHud.Reset();
        ClearTargetHudState();

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

            if (type == "ui.client_preferences.capture_keybind")
            {
                if (_keybindCapture.Begin(document.RootElement))
                {
                    _player?.SuspendForKeyCapture();
                    SendWebUi(
                        "game.client_preferences.key_capture",
                        new
                        {
                            action = _keybindCapture.PendingAction?.ToString(),
                            status = "Capturing",
                        });
                }
                else
                {
                    SendWebUi(
                        "game.client_preferences.key_capture",
                        new { status = "InvalidRequest" });
                }
                return;
            }

            if (type == "ui.client_preferences.cancel_key_capture")
            {
                if (_keybindCapture.Cancel())
                {
                    _player?.ResumeAfterKeyCapture();
                    SendWebUi(
                        "game.client_preferences.key_capture",
                        new { status = "Cancelled" });
                }
                return;
            }

            var clientUpdate = _clientSettings.Apply(
                type,
                document.RootElement);
            if (clientUpdate != ClientPreferenceUpdate.NotHandled)
            {
                if (clientUpdate is
                    ClientPreferenceUpdate.Changed or
                    ClientPreferenceUpdate.SaveFailed)
                {
                    SendClientPreferences();
                    if (type is "ui.client_preferences.hide_hints" or
                        "ui.client_preferences.hint")
                    {
                        if (_worldSeed is not null && _player is not null)
                        {
                            SendTargetHudState(force: true);
                        }
                    }
                    if (type == "ui.client_preferences.keybind")
                    {
                        _player?.ClearGameplayInput();
                    }
                }

                if (clientUpdate is not
                    (ClientPreferenceUpdate.Changed or
                     ClientPreferenceUpdate.Unchanged))
                {
                    SendWebUi(
                        "game.client_preferences.error",
                        new { code = clientUpdate.ToString() });
                }

                return;
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
                case "ui.app.exit":
                    GetTree().Quit();
                    break;
                case "ui.world.catalog.refresh":
                    if (_worldSeed is null)
                        BeginWorldCatalogScan();
                    break;
                case "ui.world.catalog.open_folder":
                    if (_worldSeed is null &&
                        !_worldCatalog.TryOpenFolder(out var openFolderError))
                    {
                        GD.PushWarning($"world.catalog.open_folder: {openFolderError}");
                        SendWebUi("game.world_catalog.folder_error", new { });
                    }
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
                case "ui.player.set_game_mode":
                    SetRequestedGameMode(document.RootElement);
                    break;
                case "ui.world.set_ticks":
                    SetRequestedWorldTicks(document.RootElement);
                    break;
                case "ui.game.resume":
                    if (_worldReadySent && !_keybindCapture.IsCapturing &&
                        !_inventoryOpen && !_chat.IsOpen)
                    {
                        _player?.ResumeGameplay();
                    }
                    break;
                case "ui.chat.close":
                    CloseChat();
                    break;
                case "ui.chat.submit":
                    SubmitChat(document.RootElement);
                    break;
                case "ui.inventory.close":
                    CloseInventory();
                    break;
                case "ui.brush.select":
                    SelectBrushDye(document.RootElement);
                    break;
                case "ui.brush.close":
                    CloseBrushPalette();
                    break;
                case "ui.inventory.slot":
                    HandleInventorySlot(document.RootElement);
                    break;
                case "ui.inventory.sort":
                    if (_inventoryOpen && _sessionStates.Player.Inventory.SortBackpack())
                        SendInventoryState();
                    break;
                case "ui.inventory.discard_cursor":
                    if (_inventoryOpen && _sessionStates.Player.Inventory.DiscardCursor())
                        SendInventoryState();
                    break;
                case "ui.inventory.creative_pick":
                    HandleCreativePick(document.RootElement);
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
        SendClientPreferences();

        if (_worldSeed is null)
        {
            SendWorldCreationState();
            BeginWorldCatalogScan();
            return;
        }

        SendPlayerModeState();
        SendWorldSettings();
        SendHotbarState();
        SendInventoryState();
        SendBrushPalette();
        SendCreativeCatalog();
        SendChatState();
        SendWorldHudState(
            force: true);
        SendWorldClockState(force: true);
        SendFpsHudState();
        SendTargetHudState(
            force: true);

        if (_loading.IsActive)
        {
            SendLoadingState(
                force: true);
        }

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

    private void SetRequestedGameMode(JsonElement message)
    {
        if (_worldSeed is null ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("mode", out var modeValue) ||
            modeValue.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<PlayerGameMode>(
                modeValue.GetString(), ignoreCase: false,
                out var requestedMode) ||
            !Enum.IsDefined(requestedMode))
        {
            SendWebUi(
                "game.player_mode.error",
                new { code = "InvalidGameMode" });
            return;
        }

        if (_inventoryOpen && requestedMode.IsSpectator())
        {
            if (!_sessionStates.Player.Inventory.TryReturnCursor())
            {
                SendWebUi("game.inventory.error",
                    new { code = "InventoryFull" });
                return;
            }
            _inventoryOpen = false;
            _player?.ResumeAfterKeyCapture();
            SendInventoryState();
        }

        if (_player is not null &&
            _sessionStates.Player.GameMode.IsSpectator() &&
            !requestedMode.IsSpectator() &&
            (!PlayerCollisionVolumeIsResident(_player) ||
             !_player.CanEnterSolidMode()))
        {
            SendWebUi(
                "game.player_mode.error",
                new { code = "NoSafeCollisionSpace" });
            return;
        }

        if (!_sessionStates.Player.SetGameMode(requestedMode))
        {
            return;
        }

        _player?.ApplyGameMode();
        if (requestedMode.IsSpectator())
        {
            ClearTargetHudState();
        }
        else if (_player is not null)
        {
            SendTargetHudState(force: true);
        }

        SendPlayerModeState();
        SendWorldSettings();
        SendInventoryState();
    }

    private bool PlayerCollisionVolumeIsResident(FpsPlayer player)
    {
        var volume = player.CollisionBounds;
        var minX = Mathf.FloorToInt(volume.Minimum.X);
        var minY = Mathf.FloorToInt(volume.Minimum.Y);
        var minZ = Mathf.FloorToInt(volume.Minimum.Z);
        var maxX = Mathf.CeilToInt(volume.Maximum.X) - 1;
        var maxY = Mathf.CeilToInt(volume.Maximum.Y) - 1;
        var maxZ = Mathf.CeilToInt(volume.Maximum.Z) - 1;

        if (minY < 0)
        {
            return false;
        }

        // The player capsule is less than two blocks wide and high:
        // this checks only its small, bounded voxel neighborhood.
        for (var x = minX; x <= maxX; x++)
        for (var y = minY; y <= maxY; y++)
        for (var z = minZ; z <= maxZ; z++)
        {
            if (!_world.IsLoadedAt(new WorldVoxelCoord(x, y, z)))
            {
                return false;
            }
        }

        return true;
    }

    private void SetRequestedWorldTicks(JsonElement message)
    {
        if (_worldSeed is null ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("value", out var raw) ||
            raw.ValueKind != JsonValueKind.Number ||
            !raw.TryGetUInt32(out var rate) ||
            rate == 0)
        {
            SendWebUi(
                "game.world_settings.error",
                new { code = "InvalidTickRate" });
            return;
        }

        if (_sessionStates.GameRules.SetTicksPerSecond(rate))
        {
            SendWorldSettings();
        }
    }

    private void SendWorldSettings()
    {
        if (_worldSeed is null) return;
        SendWebUi(
            "game.world_settings",
            new
            {
                name = _sessionStates.Name,
                mode = _sessionStates.Player.GameMode.ToString(),
                ticksPerSecond = _sessionStates.GameRules.TicksPerSecond,
                spawnCreatures = _sessionStates.GameRules.SpawnCreatures,
            });
    }

    private void SendPlayerModeState()
    {
        if (_worldSeed is null) return;
        var player = _sessionStates.Player;
        SendWebUi(
            "game.player_mode",
            new
            {
                mode = player.GameMode.ToString().ToLowerInvariant(),
                flying = player.IsFlying,
                canInteract = player.CanInteract,
            });
    }

    private void SendClientPreferences()
    {
        // Keep the same camelCase/string-enum JSON contract used by
        // persisted settings, without a second handwritten DTO.
        using var document = JsonDocument.Parse(
            ClientPreferencesJson.Serialize(_clientPreferences));
        SendWebUi(
            "game.client_preferences",
            document.RootElement);
    }

    private void BeginWorldCatalogScan()
    {
        if (_worldCatalog.Begin())
            SendWebUi("game.world_catalog", new { status = "verifying" });
    }

    private void PollWorldCatalog()
    {
        if (!_worldCatalog.TryPoll(out var worlds, out var error) ||
            _worldSeed is not null)
            return;

        if (error is not null)
        {
            GD.PushWarning($"world.catalog.scan: {error}");
            SendWebUi("game.world_catalog", new { status = "error" });
            return;
        }

        SendWebUi(
            "game.world_catalog",
            new
            {
                status = "ready",
                worlds = worlds.Select(world => new
                {
                    id = world.Id,
                    lastSaved = world.LastSaved,
                    seed = world.Seed,
                    daysPassed = world.DaysPassed,
                    sphere = world.Sphere,
                    coordinates = world.Coordinates,
                    compatible = world.Compatible,
                }).ToArray(),
            });
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
                "newWorld.error.seedMustBeString");
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
                "newWorld.error.invalidSeed");
            return;
        }

        var name = payload.TryGetProperty(
                "name", out var nameElement) &&
            nameElement.ValueKind == JsonValueKind.String
                ? nameElement.GetString()!
                : WorldCreationOptions.DefaultName;
        var rawMode = payload.TryGetProperty(
            "mode", out var modeElement) &&
            modeElement.ValueKind == JsonValueKind.String
                ? modeElement.GetString()
                : nameof(PlayerGameMode.Survival);
        var ticks = payload.TryGetProperty(
            "ticksPerSecond", out var ticksElement) &&
            ticksElement.ValueKind == JsonValueKind.Number &&
            ticksElement.TryGetUInt32(out var parsedTicks)
                ? parsedTicks
                : 0u;

        if (!Enum.TryParse<PlayerGameMode>(
                rawMode, ignoreCase: false, out var mode) ||
            !Enum.IsDefined(mode))
        {
            SendWorldCreationError("newWorld.error.invalidMode");
            return;
        }

        try
        {
            var creation = new WorldCreationOptions(
                name,
                selectedSeed,
                mode,
                ticks);
            StartWorld(creation);
        }
        catch (ArgumentException exception)
        {
            SendWorldCreationError(
                exception.ParamName == "name"
                    ? "newWorld.error.invalidName"
                    : "newWorld.error.invalidTickRate");
        }
    }

    private void StartWorld(
        WorldCreationOptions creation)
    {
        if (_worldSeed is not null)
        {
            return;
        }

        _sessionStates =
            new DimensionSessionStateStore(
                creation,
                _dimensions);
        _sessions =
            new DimensionSessionController(
                _sessionStates,
                CreateDimensionSession);
        _sessions.Start(
            new DimensionId(
                StartupDimensionId));
        ActivateCurrentDimensionPresentation();

        // Every world starts with an empty, authoritative player inventory.
        // Creative obtains blocks from the actual authored content catalog.
        SyncHeldBlock();
        _worldSeed =
            creation.Seed;

        _webUi.Call(
            "set_creation_mode",
            false);
        SendPlayerModeState();
        SendWorldSettings();
        SendWebUi(
            "game.world_creation.started",
            new
            {
                seed =
                    WorldCreationSeed.Format(
                        creation.Seed),
            });
        SendHotbarState();
        SendInventoryState();
        SendCreativeCatalog();

        _worldDiagnostics?.WorldStarted(creation.Seed, _dimension.Id);
        GD.Print(
            $"world.start seed={creation.Seed} " +
            $"dimension={_dimension.Id} " +
            $"dimension_seed={_dimensionSeed}");
        GD.Print(
            $"streaming: render_distance={_clientPreferences.RenderDistanceChunks} " +
            $"retention_margin={RetentionMarginChunks} " +
            $"materialization_in_flight={MaxMaterializationTasksInFlight}");

        BeginWorldLoading();
    }

    private void BeginWorldLoading()
    {
        _worldReadySent = false;
        _loading.Begin(
            _spawnChunk);
        _lastLoadingProgress = null;

        ReportStreamingSelection(
            _chunkStreaming.SyncSelection(
                _loading.Center,
                WorldLoadingState.InitialHorizontalRadiusChunks));
        SendLoadingState();
    }

    private void AdvanceWorldLoading()
    {
        _loading.UpdateResidency(
            _residency.DesiredCount,
            _residency.PendingCount,
            _residency.MaterializingCount,
            _chunkStreaming.IsSelectionRunning);

        if (_loading.Progress.Phase ==
            WorldLoadingPhase.PreparingPresentation)
        {
            _ = _loading.UpdatePresentation(
                _chunkPresentations.IsFullyPublished(
                    _spawnChunk));
        }

        SendLoadingState();

        if (!_loading.Progress.IsReady)
        {
            return;
        }

        _worldReadySent = true;
        SetupPlayer();
        SendLoadingState(
            force: true);
        SendWorldReady();

        _loading.Reset();
        _lastLoadingProgress = null;

        ReportStreamingSelection(
            _chunkStreaming.SyncSelection(
                CurrentStreamingCenter(),
                _clientPreferences.RenderDistanceChunks));
    }

    private void SendLoadingState(
        bool force = false)
    {
        var progress =
            _loading.Progress;

        if (progress.Phase ==
            WorldLoadingPhase.Inactive)
        {
            return;
        }

        if (!force &&
            _lastLoadingProgress is
                { } previous &&
            previous ==
                progress)
        {
            return;
        }

        _lastLoadingProgress =
            progress;

        var phase =
            progress.Phase switch
            {
                WorldLoadingPhase.RetiringCurrentDimension =>
                    "retiring_current_dimension",
                WorldLoadingPhase.MaterializingInitialArea =>
                    "materializing_initial_area",
                WorldLoadingPhase.PreparingPresentation =>
                    "preparing_presentation",
                WorldLoadingPhase.Ready =>
                    "ready",
                _ =>
                    throw new InvalidOperationException(
                        $"Unsupported loading phase {progress.Phase}."),
            };

        SendWebUi(
            "game.loading",
            new
            {
                phase,
                completed =
                    progress.Completed,
                total =
                    progress.Total,
                dimension =
                    _dimension.Id.Value,
            });
    }

    private void SendWorldCreationError(
        string key)
    {
        SendWebUi(
            "game.world_creation.error",
            new { key });
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
                _sessions.Active.Generator.EffectiveBiomeAt,
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

    private static object? InventorySlotView(InventoryStack? stack) =>
        stack is null ? null : new
        {
            id = stack.Id,
            kind = stack.Kind.ToString().ToLowerInvariant(),
            quantity = stack.Quantity,
            metadata = stack.Entry.Metadata,
        };

    private void SendHotbarState()
    {
        var inventory = _sessionStates.Player.Inventory;
        SendWebUi(
            "game.hud.hotbar",
            new
            {
                selectedIndex = inventory.SelectedSlot,
                slots = Enumerable.Range(0, PlayerInventory.HotbarSlots)
                    .Select(index => InventorySlotView(
                        inventory.SlotAt(PlayerInventory.BackpackSlots + index)))
                    .ToArray(),
            });
        SendArtisansKitState();
    }

    private void SendArtisansKitState()
    {
        var kit = _sessions.Active.ArtisansKit;
        SendWebUi("game.tool.artisans_kit", new
        {
            resolution = kit.IsEquipped(
                _sessionStates.Player.Inventory.SelectedStack)
                ? (int?)kit.Resolution : null,
        });
    }

    private void SendInventoryState()
    {
        if (_worldSeed is null) return;
        var inventory = _sessionStates.Player.Inventory;
        SendWebUi("game.inventory.state", new
        {
            open = _inventoryOpen,
            creativeAvailable = _sessionStates.Player.GameMode ==
                PlayerGameMode.Creative,
            selectedIndex = inventory.SelectedSlot,
            backpack = Enumerable.Range(0, PlayerInventory.BackpackSlots)
                .Select(i => InventorySlotView(inventory.SlotAt(i))).ToArray(),
            hotbar = Enumerable.Range(0, PlayerInventory.HotbarSlots)
                .Select(i => InventorySlotView(
                    inventory.SlotAt(PlayerInventory.BackpackSlots + i)))
                .ToArray(),
            cursor = InventorySlotView(inventory.Cursor),
        });
    }

    private void SendCreativeCatalog()
    {
        SendWebUi("game.inventory.catalog", new
        {
            items = _inventoryCatalog.Choices
                .Select(choice => new
                {
                    id = choice.Entry.Id,
                    kind = choice.Entry.Kind.ToString().ToLowerInvariant(),
                    name = choice.Entry.Id,
                    category = choice.Entry.Kind.ToString().ToLowerInvariant() +
                        "/" + choice.Category,
                    metadata = choice.Entry.Metadata,
                    iconUrl = choice.IconResourcePath is { } path
                        ? IconDataUri(path) : null,
                })
                .ToArray(),
        });
    }

    private string IconDataUri(string resourcePath)
    {
        if (_inventoryIconCache.TryGetValue(resourcePath, out var cached))
            return cached;

        if (!resourcePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"Inventory icon must be PNG: {resourcePath}");
        var absolute = ProjectPackFiles.AbsoluteResourcePath(
            _packSelection, resourcePath);
        var info = new FileInfo(absolute);
        if (info.Length is < 1 or > 131072)
            throw new InvalidDataException(
                $"Inventory icon exceeds allowed size: {resourcePath}");
        var encoded = "data:image/png;base64," +
            Convert.ToBase64String(File.ReadAllBytes(absolute));
        _inventoryIconCache.Add(resourcePath, encoded);
        return encoded;
    }

    private void SyncHeldBlock()
    {
        var selected = _sessionStates.Player.Inventory.SelectedStack;
        if (_sessions.Active.ArchitectsCompass.ClearUnlessEquipped(
                selected, _sessionStates.Player.Inventory.SelectedSlot))
            _sessions.Active.StructureSelection.Hide();
        if (selected?.Block is not { } selectedBlock)
        {
            _sessionStates.Player.HeldBlock.Clear();
            return;
        }
        var block = selectedBlock.Cell.Block;
        _sessionStates.Player.HeldBlock.Select(
            selectedBlock, _blocks.GetDefinition(block));
    }

    private void SelectHotbar(int index)
    {
        var inventory = _sessionStates.Player.Inventory;
        if (index == -1)
            index = (inventory.SelectedSlot + PlayerInventory.HotbarSlots - 1) %
                PlayerInventory.HotbarSlots;
        else if (index == -2)
            index = (inventory.SelectedSlot + 1) % PlayerInventory.HotbarSlots;
        if (!inventory.SelectHotbar(index)) return;
        SyncHeldBlock();
        SendHotbarState();
        SendInventoryState();
        SendTargetHudState(force: true);
    }

    private void OpenBrushPalette()
    {
        if (!_worldReadySent || _inventoryOpen || _brushPaletteOpen ||
            _sessions.IsTransitioning || _player is null ||
            !_sessionStates.Player.CanInteract)
            return;
        _brushPaletteOpen = true;
        SendBrushPalette();
        _player.SuspendForModal();
    }

    private void SendBrushPalette()
    {
        if (_worldSeed is null) return;
        var brush = _sessions.Active.Tools;
        SendWebUi("game.tool.brush_palette", new
        {
            open = _brushPaletteOpen,
            selectedId = brush.SelectedBrushDyeId,
            colors = brush.BrushPalette.Select(dye => new
            {
                id = dye.Id,
                rgb = $"#{(byte)MathF.Round(dye.Rgb.X * 255f):x2}{(byte)MathF.Round(dye.Rgb.Y * 255f):x2}{(byte)MathF.Round(dye.Rgb.Z * 255f):x2}"
            }).ToArray()
        });
    }

    private void SelectBrushDye(JsonElement message)
    {
        if (!_brushPaletteOpen || _sessions.IsTransitioning ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("id", out var idValue) ||
            (idValue.ValueKind != JsonValueKind.Null &&
             idValue.ValueKind != JsonValueKind.String))
            return;

        var id = idValue.ValueKind == JsonValueKind.Null ? null : idValue.GetString();
        if (_sessions.Active.Tools.TrySelectBrushDye(id))
            CloseBrushPalette();
    }

    private void CloseBrushPalette()
    {
        if (!_brushPaletteOpen) return;
        _brushPaletteOpen = false;
        SendBrushPalette();
        CallDeferred(nameof(ResumeGameplayAfterInventory));
    }

    private void OpenChat()
    {
        if (!_worldReadySent || _inventoryOpen || _brushPaletteOpen ||
            _keybindCapture.IsCapturing || _sessions.IsTransitioning ||
            _player is null || !_player.IsMouseCaptured || !_chat.Open())
            return;

        SendChatState();
        _player.SuspendForModal();
    }

    private void CloseChat()
    {
        if (!_chat.Close()) return;
        _chatFeedbackSeconds = _chat.History.Count > 0 ? 10.0 : 0.0;
        SendChatState();
        _player?.ResumeAfterKeyCapture();
        CallDeferred(nameof(ResumeGameplayAfterChat));
    }

    private void ResumeGameplayAfterChat()
    {
        if (!_chat.IsOpen && !_inventoryOpen && !_brushPaletteOpen &&
            !_keybindCapture.IsCapturing && _worldReadySent &&
            !_sessions.IsTransitioning)
            _player?.ResumeGameplay();
    }

    private void SubmitChat(JsonElement message)
    {
        if (!_chat.IsOpen || !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("text", out var value) ||
            value.ValueKind != JsonValueKind.String)
            return;

        if (_chat.TrySubmit(value.GetString(), out var text))
        {
            _chatFeedbackSeconds = 10.0;
            ExecuteChatCommand(PlayerChatCommandProcessor.Parse(text));
        }

        // Invalid/empty submissions also close the chat, without echoing.
        SendChatState();
        _player?.ResumeAfterKeyCapture();
        CallDeferred(nameof(ResumeGameplayAfterChat));
    }

    private void ExecuteChatCommand(ParsedChatCommand command)
    {
        if (_sessionStates.Player.GameMode.IsSpectator() &&
            command.Kind != ChatCommandKind.Say)
        {
            ChatFeedback("chat.command.spectatorUnavailable", error: true);
            return;
        }

        switch (command.Kind)
        {
            case ChatCommandKind.Say:
                _chat.Append($"<Player>: {command.Argument}");
                break;
            case ChatCommandKind.Usage:
                ChatFeedback("chat.command.usage", error: true,
                    ("usage", command.Argument ?? ""));
                break;
            case ChatCommandKind.Unknown:
                ChatFeedback("chat.command.unknown", error: true,
                    ("command", command.Argument ?? ""));
                break;
            case ChatCommandKind.Spawn:
                ExecuteChatSpawn(command);
                break;
            case ChatCommandKind.LocateBiome:
            case ChatCommandKind.LocateStructure:
                ExecuteChatLocate(command);
                break;
            case ChatCommandKind.Kill:
                ExecuteChatKill();
                break;
            case ChatCommandKind.Warp:
                ExecuteChatWarp(command);
                break;
            default:
                ChatFeedback("chat.command.notImplemented", error: true,
                    ("command", command.Kind.ToString().ToLowerInvariant()));
                break;
        }
    }

    private void ChatFeedback(
        string key, bool error = false,
        params (string Key, string Value)[] parameters)
    {
        var arguments = parameters.ToDictionary(
            pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        _chat.Append(key, error, key, arguments);
    }

    private void ExecuteChatWarp(ParsedChatCommand command)
    {
        if (_player is null || command.Y < 0 || _sessions.IsTransitioning)
        {
            ChatFeedback("chat.command.warp.failed", error: true);
            return;
        }

        var dimension = command.Option ?? _dimension.Id.Value;
        var target = _dimensions.Definitions().FirstOrDefault(
            definition => definition.Id.Value == dimension);
        if (target is null)
        {
            ChatFeedback("chat.command.warp.failed", error: true);
            return;
        }

        var position = new NVector3(
            command.X + 0.5f, command.Y, command.Z + 0.5f);
        if (target.Id != _dimension.Id)
        {
            if (!BeginDimensionTransition(target.Id, position))
                ChatFeedback("chat.command.warp.failed", error: true);
            else
                ChatFeedback("chat.command.warp.start", error: false,
                    ("position", $"({command.X}, {command.Z}, {command.Y})"));
            return;
        }

        // Do not teleport into an unloaded or mutated/occupied voxel.
        // A long-range same-Sphere warp requires the streaming/entry path.
        var safe = _sessions.Active.Generator.FindGeneratedDestination(
            command.X, command.Y, command.Z, maxRadius: 16);
        if (safe is not { } location)
        {
            ChatFeedback("chat.command.warp.failed", error: true);
            return;
        }
        var feet = new WorldVoxelCoord(location.X, location.Y, location.Z);
        var head = new WorldVoxelCoord(location.X, location.Y + 1, location.Z);
        if (!_world.IsLoadedAt(feet) || !_world.IsLoadedAt(head) ||
            !_world.GetCellOrEmpty(feet).IsEmpty ||
            !_world.GetCellOrEmpty(head).IsEmpty)
        {
            ChatFeedback("chat.command.warp.failed", error: true);
            return;
        }

        _player.ClearGameplayInput();
        _player.GlobalPosition = new Vector3(
            location.X + 0.5f, location.Y, location.Z + 0.5f);
        ChatFeedback("chat.command.warp.success", error: false,
            ("position", $"({location.X}, {location.Z}, {location.Y})"));
        SendWorldHudState(force: true);
    }

    private void ExecuteChatKill()
    {
        if (_player is null)
        {
            ChatFeedback("chat.command.target.none", error: true);
            return;
        }

        var (from, to) = _player.GetInteractionRay(InteractionDistance);
        var origin = new NVector3(from.X, from.Y, from.Z);
        var direction = new NVector3(
            to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        var target = _sessions.Active.FindCreatureTarget(
            origin, direction, InteractionDistance);
        if (target is not { } hit)
        {
            ChatFeedback("chat.command.target.none", error: true);
            return;
        }

        if (!_sessions.Active.TryKillCreature(hit.Creature.Id, out var result))
        {
            ChatFeedback("chat.command.target.unavailable", error: true);
            return;
        }

        ChatFeedback("chat.command.kill.success", error: false,
            ("name", result.DefinitionId),
            ("position", $"({Mathf.FloorToInt(result.Position.X)}, " +
                $"{Mathf.FloorToInt(result.Position.Z)}, " +
                $"{Mathf.FloorToInt(result.Position.Y)})"));
        SendTargetHudState(force: true);
    }

    private void ExecuteChatSpawn(ParsedChatCommand command)
    {
        var id = command.Argument!;
        if (!_creatures.TryGet(id, out _))
        {
            ChatFeedback("chat.command.spawn.unknownCreature", error: true,
                ("id", id));
            return;
        }

        if (command.Option is not null)
        {
            ChatFeedback("chat.command.notImplemented", error: true,
                ("command", "/spawn <id> [meta_tag]"));
            return;
        }

        if (_player is null)
        {
            ChatFeedback("chat.command.spawn.playerUnavailable", error: true);
            return;
        }

        var (_, target) = _player.GetInteractionRay(5f);
        var safe = _sessions.Active.Generator.FindGeneratedDestination(
            Mathf.FloorToInt(target.X),
            Math.Max(0, Mathf.FloorToInt(target.Y)),
            Mathf.FloorToInt(target.Z), maxRadius: 12);
        if (safe is null ||
            !_world.IsLoadedAt(new WorldVoxelCoord(
                safe.Value.X, safe.Value.Y, safe.Value.Z)))
        {
            ChatFeedback("chat.command.spawn.noSpace", error: true, ("id", id));
            return;
        }

        var feet = new NVector3(
            safe.Value.X + 0.5f, safe.Value.Y, safe.Value.Z + 0.5f);
        if (!_sessions.Active.TrySpawnCreature(id, feet))
        {
            ChatFeedback("chat.command.spawn.failed", error: true, ("id", id));
            return;
        }

        ChatFeedback("chat.command.spawn.success", error: false,
            ("name", id),
            ("position", $"({safe.Value.X}, {safe.Value.Z}, {safe.Value.Y})"));
    }

    private void ExecuteChatLocate(ParsedChatCommand command)
    {
        if (_player is null)
        {
            ChatFeedback("chat.command.locate.playerUnavailable", error: true);
            return;
        }

        var id = command.Argument!;
        string? structureId = null;
        if (command.Kind == ChatCommandKind.LocateStructure &&
            command.Option is { } variationText)
        {
            if (!_structures.ResolvesReference(id) ||
                !PlayerChatCommandProcessor.TryVariation(variationText,
                    out var variation))
            {
                ChatFeedback("chat.command.locate.unknownStructure",
                    error: true, ("id", id));
                return;
            }

            var members = _structures.ResolveReference(id);
            if (variation > members.Count)
            {
                ChatFeedback("chat.command.locate.unknownStructure",
                    error: true, ("id", id));
                return;
            }
            structureId = members[variation - 1].Id;
        }

        if (command.Kind == ChatCommandKind.LocateBiome)
        {
            if (!_biomes.Contains(id))
            {
                ChatFeedback("chat.command.locate.unknownBiome", error: true,
                    ("id", id));
                return;
            }
            if (!_dimension.SurfaceBiomes.Contains(id, StringComparer.Ordinal))
            {
                ChatFeedback("chat.command.locate.biomeInactive", error: true,
                    ("id", id));
                return;
            }
        }

        var origin = _player.GlobalPosition;
        if (!_chatLocate.Begin(_sessions.Active.Generator,
                command.Kind, id,
                Mathf.FloorToInt(origin.X), Mathf.FloorToInt(origin.Z),
                structureId))
        {
            ChatFeedback("chat.command.failed", error: true);
            return;
        }
        ChatFeedback("chat.command.locate.searching", error: false,
            ("name", id));
    }

    private void PollChatLocate()
    {
        var generator = _worldSeed is null || _sessions.IsTransitioning
            ? null : _sessions.Active.Generator;
        if (!_chatLocate.TryPoll(generator, out var found,
                out var error, out var searchedId, out var stale) || stale)
            return;

        if (error is not null)
        {
            GD.PushWarning($"chat.locate error: {error}");
            ChatFeedback("chat.command.failed", error: true);
        }
        else if (found is { } result)
        {
            ChatFeedback("chat.command.locate.found", error: false,
                ("name", result.Id),
                ("position", $"({result.X}, {result.Z}, {result.Y})"));
        }
        else
        {
            ChatFeedback("chat.command.locate.notFound", error: true,
                ("name", searchedId), ("radius", "512"));
        }

        _chatFeedbackSeconds = 10.0;
        SendChatState();
    }

    private void AdvanceChatPresentation(double delta)
    {
        if (_chat.IsOpen || _chatFeedbackSeconds <= 0.0) return;
        _chatFeedbackSeconds = Math.Max(0.0, _chatFeedbackSeconds - delta);
        if (_chatFeedbackSeconds == 0.0) SendChatState();
    }

    private void SendChatState()
    {
        SendWebUi("game.chat.state", new
        {
            open = _chat.IsOpen,
            visible = _chat.IsOpen || _chatFeedbackSeconds > 0.0,
            history = _chat.History.Select(line => new
            {
                id = line.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                text = line.Text,
                tone = line.IsError ? "error" : "normal",
                localizationKey = line.LocalizationKey,
                parameters = line.Parameters,
            }).ToArray(),
            commands = PlayerChatCommandProcessor.SupportedCommands,
        });
    }

    private void OpenInventory()
    {
        if (!_worldReadySent || _inventoryOpen || _chat.IsOpen ||
            _sessions.IsTransitioning ||
            _sessionStates.Player.GameMode.IsSpectator() || _player is null)
            return;
        _inventoryOpen = true;
        _player.SuspendForModal();
        SendInventoryState();
    }

    private void CloseInventory()
    {
        if (!_inventoryOpen) return;
        if (!_sessionStates.Player.Inventory.TryReturnCursor())
        {
            SendWebUi("game.inventory.error",
                new { code = "InventoryFull" });
            return;
        }
        _inventoryOpen = false;
        SyncHeldBlock();
        SendInventoryState();
        SendHotbarState();
        _player?.ResumeAfterKeyCapture();
        CallDeferred(nameof(ResumeGameplayAfterInventory));
    }

    private void ResumeGameplayAfterInventory()
    {
        if (!_inventoryOpen && !_brushPaletteOpen &&
            !_keybindCapture.IsCapturing)
            _player?.ResumeGameplay();
    }

    private void HandleInventorySlot(JsonElement message)
    {
        if (!_inventoryOpen ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("index", out var indexValue) ||
            !indexValue.TryGetInt32(out var index)) return;
        if (!_sessionStates.Player.Inventory.ClickSlot(index)) return;
        SyncHeldBlock();
        SendHotbarState();
        SendInventoryState();
    }

    private void HandleCreativePick(JsonElement message)
    {
        if (!_inventoryOpen ||
            _sessionStates.Player.GameMode != PlayerGameMode.Creative ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("id", out var idValue) ||
            idValue.ValueKind != JsonValueKind.String) return;
        var id = idValue.GetString();
        if (id is null ||
            !payload.TryGetProperty("kind", out var kindValue) ||
            kindValue.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<InventoryEntryKind>(
                kindValue.GetString(), true, out var kind) ||
            !Enum.IsDefined(kind)) return;

        var metadataKey = (string?)null;
        var metadataValue = (string?)null;
        if (payload.TryGetProperty("metadata", out var metadata))
        {
            if (metadata.ValueKind != JsonValueKind.Object) return;
            var properties = metadata.EnumerateObject().ToArray();
            if (properties.Length != 1 ||
                properties[0].Value.ValueKind != JsonValueKind.String) return;
            metadataKey = properties[0].Name;
            metadataValue = properties[0].Value.GetString();
        }

        if (!_inventoryCatalog.TryResolve(
                kind, id, metadataKey, metadataValue, out var entry) ||
            entry is null ||
            !_sessionStates.Player.Inventory.TryCreativePick(entry)) return;
        SendInventoryState();
    }

    private void DropSelectedItem()
    {
        if (_player is null || _inventoryOpen ||
            !_sessionStates.Player.CanInteract) return;
        var inventory = _sessionStates.Player.Inventory;
        var selected = inventory.SelectedStack;
        if (selected is null) return;
        // Consume only after the drop is successfully registered in the
        // authoritative dimension entity simulation.
        var ray = _player.GetInteractionRay(2f);
        var direction = (ray.To - ray.From).Normalized();
        var from = ray.From + direction * 0.6f;
        _blockEntities.SpawnPlayerDrop(
            selected.WithQuantity(1),
            new NVector3(from.X, MathF.Max(from.Y, 0.2f), from.Z),
            new NVector3(direction.X * 3f,
                direction.Y * 3f + 2f, direction.Z * 3f));
        if (!inventory.TryConsumeSelected())
            throw new InvalidOperationException(
                "Selected inventory stack changed during player drop.");
        SyncHeldBlock();
        SendHotbarState();
        SendInventoryState();
    }

    private void AdvanceItemPickup(double delta)
    {
        if (_player is null || _blockEntities.DroppedCount == 0 ||
            _sessionStates.Player.GameMode.IsSpectator())
        {
            _pickupAccumulator = 0;
            return;
        }

        _pickupAccumulator += delta;
        if (_pickupAccumulator < 0.1) return;
        _pickupAccumulator = 0;

        var player = _player.GlobalPosition + new Vector3(0f, 0.9f, 0f);
        var collected = _blockEntities.CollectNearby(
            new NVector3(player.X, player.Y, player.Z),
            1.2f,
            _sessionStates.Player.Inventory.TryInsert);
        if (collected == 0) return;
        SyncHeldBlock();
        SendHotbarState();
        SendInventoryState();
    }

    private void AdvanceFpsHudState(
        double delta)
    {
        if (!_fpsHud.TryAdvance(
                delta,
                out var fps))
        {
            return;
        }

        SendFpsHudState(
            fps);
    }

    private void SendFpsHudState()
    {
        if (_fpsHud.Current is
            { } fps)
        {
            SendFpsHudState(
                fps);
        }
    }

    private void SendFpsHudState(
        int fps)
    {
        SendWebUi(
            "game.hud.fps",
            new { fps });
    }

    private void SendTargetHudState(
        bool force = false)
    {
        if (_sessionStates.Player.GameMode.IsSpectator())
            return;

        CreatureTargetHit? creatureHit = null;
        if (_player is { IsMouseCaptured: true } creaturePlayer)
        {
            var (from, to) = creaturePlayer.GetInteractionRay(InteractionDistance);
            creatureHit = _sessions.Active.FindCreatureTarget(
                new NVector3(from.X, from.Y, from.Z),
                new NVector3(to.X - from.X, to.Y - from.Y, to.Z - from.Z),
                InteractionDistance);
        }

        if (creatureHit is { } target)
        {
            var creature = target.Creature;
            var maximum = _creatures.Get(creature.DefinitionId).Health;
            var creatureSnapshot = (creature.Id.Value, creature.Health, (float)maximum);
            if (force || _lastCreatureHud != creatureSnapshot)
            {
                SendWebUi("game.hud.target_entity", new
                {
                    name = creature.DefinitionId,
                    health = new
                    {
                        current = creature.Health,
                        maximum,
                    },
                });
                // Do not allow a stale block target to reappear underneath.
                SendWebUi("game.hud.target", new { });
                SendWebUi("game.hud.prompt", new { });
                _lastCreatureHud = creatureSnapshot;
            }
            _targetHud.Reset();
            return;
        }

        if (_lastCreatureHud is not null)
        {
            _lastCreatureHud = null;
            SendWebUi("game.hud.target_entity", new { });
        }

        var hit =
            _player is
                { IsMouseCaptured: true }
                ? CurrentTarget()
                : null;

        if (!_targetHud.TryCapture(
                _world,
                _blocks,
                hit,
                force,
                out var snapshot))
        {
            return;
        }

        if (snapshot is null)
        {
            SendWebUi(
                "game.hud.target",
                new { });
            SendWebUi(
                "game.hud.prompt",
                new { });
            return;
        }

        var value =
            snapshot.Value;

        SendWebUi(
            "game.hud.target",
            new
            {
                kind = "block",
                id = value.BlockId,
                name = value.BlockId,
                details = new[]
                {
                    $"Sky Light: {value.SkyLight} | Block Light: {value.BlockLight}",
                    $"X: {value.X} | Z: {value.Z} | Y: {value.Y}",
                },
            });
        var breakHint = _clientPreferences.HintVisible(
            HintKind.BreakOrPlaceBlock);
        var rotateHint = _sessionStates.Player.HeldBlock.CanRotate &&
            _clientPreferences.HintVisible(HintKind.RotateBlock);

        if (!breakHint && !rotateHint)
        {
            SendWebUi("game.hud.prompt", new { });
            return;
        }

        SendWebUi(
            "game.hud.prompt",
            new
            {
                key = breakHint && rotateHint
                    ? "LMB/RMB · " + _clientPreferences.KeyFor(KeybindAction.ToolAction)
                    : breakHint
                        ? "LMB/RMB"
                        : _clientPreferences.KeyFor(KeybindAction.ToolAction).ToString(),
                text = breakHint && rotateHint
                    ? "hud.hint.breakPlaceRotate"
                    : breakHint
                        ? "hud.hint.breakPlace"
                        : "hud.hint.rotate",
            });
    }

    private void ClearTargetHudState()
    {
        _targetHud.Reset();
        _lastCreatureHud = null;
        SendWebUi("game.hud.target_entity", new { });
        SendWebUi(
            "game.hud.target",
            new { });
        SendWebUi(
            "game.hud.prompt",
            new { });
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
            PlayerState = _sessionStates.Player,
            InputPreferences = _clientPreferences,
            CurrentWorldTick = () => _worldTicks.CurrentTick,
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
        _player.ToolActionRequested += RotateHeldBlock;
        _player.InventoryRequested += OpenInventory;
        _player.ChatRequested += OpenChat;
        _player.DropItemRequested += DropSelectedItem;
        _player.HotbarSlotRequested += SelectHotbar;
        _player.MouseCaptureChanged +=
            SendMouseCaptureState;
        _player.FlightStateChanged +=
            SendPlayerModeState;
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
        SendPlayerModeState();
        SendHotbarState();
        SendInventoryState();
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
        _worldDiagnostics?.Observe(update);
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
        if (!_sessionStates.Player.CanInteract)
            return;

        // A creature before the targeted block receives the attack.
        // The Core target query owns reach, line of sight and tie breaking.
        if (_player is { IsMouseCaptured: true } player)
        {
            var (from, to) = player.GetInteractionRay(InteractionDistance);
            var origin = new NVector3(from.X, from.Y, from.Z);
            var direction = new NVector3(
                to.X - from.X, to.Y - from.Y, to.Z - from.Z);
            var creature = _sessions.Active.FindCreatureTarget(
                origin, direction, InteractionDistance);
            if (creature is { } target)
            {
                if (_sessions.Active.TryAttackCreature(
                        target.Creature.Id,
                        _attacks.Get("asteria:punch"),
                        new NVector3(
                            player.GlobalPosition.X,
                            player.GlobalPosition.Y,
                            player.GlobalPosition.Z),
                        out _))
                {
                    SendTargetHudState(force: true);
                }
                return;
            }
        }

        if (!TryGetTarget(out var hit))
            return;

        if (_blocks.GetDefinition(
                _world.GetCellOrEmpty(hit.Voxel).Block).Interaction ==
            BlockInteractionKind.Pickup)
            return;

        var held = _sessionStates.Player.Inventory.SelectedStack;
        if (_sessions.Active.ArtisansKit.IsEquipped(held))
        {
            if (TryUseArtisansKit(held, ToolUseHand.Left, hit))
                KickWorldMutationWorkers();
            return;
        }
        if (_sessions.Active.Tools.IsSpecialLeftAction(held))
        {
            if (_sessions.Active.Tools.TryUse(held, ToolUseHand.Left, hit))
                KickWorldMutationWorkers();
            return;
        }
        // Only Creative breaks on a click; Survival uses held-input work.
        if (_sessionStates.Player.GameMode == PlayerGameMode.Survival)
            return;

        var definition = _blocks.GetDefinition(
            _world.GetCellOrEmpty(hit.Voxel).Block);
        if (!_sessions.Active.Tools.CanMine(
                held, definition, _sessionStates.Player.GameMode))
            return;

        var decision =
            _blockInteractions.Break(
                hit,
                _sessionStates.Player.GameMode.BreakLootPolicy());

        if (!decision.Accepted)
        {
            return;
        }

        // Destruction creates a physical block drop; collection is handled
        // by DroppedBlockRuntime and the authoritative inventory.
        KickWorldMutationWorkers();
    }

    private void AdvanceSurvivalMining()
    {
        if (!_worldReadySent || _player is not { IsBreakHeld: true } player ||
            !_sessionStates.Player.CanInteract ||
            _sessionStates.Player.GameMode != PlayerGameMode.Survival)
        {
            _sessions.Active.Mining.Cancel();
            PublishMiningProgress();
            return;
        }

        var held = _sessionStates.Player.Inventory.SelectedStack;
        if (_sessions.Active.ArtisansKit.IsEquipped(held) ||
            _sessions.Active.Tools.IsSpecialLeftAction(held))
        {
            _sessions.Active.Mining.Cancel();
            PublishMiningProgress();
            return;
        }

        var (from, to) = player.GetInteractionRay(InteractionDistance);
        var origin = new NVector3(from.X, from.Y, from.Z);
        var direction = new NVector3(
            to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        if (_sessions.Active.FindCreatureTarget(
                origin, direction, InteractionDistance) is not null)
        {
            _sessions.Active.Mining.Cancel();
            PublishMiningProgress();
            return;
        }

        var inventory = _sessionStates.Player.Inventory;
        var completed = _sessions.Active.Mining.Advance(
            CurrentTarget(), held, inventory.SelectedSlot,
            _worldTicks.TicksThisFrame, _sessionStates.Player.GameMode);
        PublishMiningProgress();
        if (completed) KickWorldMutationWorkers();
    }

    private void PublishMiningProgress()
    {
        var progress = _sessions.Active.Mining.Progress;
        var stage = progress is { } value
            ? Math.Clamp((int)MathF.Ceiling(value * 10f), 0, 10)
            : -1;
        if (stage == _publishedMiningStage) return;
        _publishedMiningStage = stage;
        SendWebUi("game.hud.mining", new
        {
            progress = stage < 0 ? (float?)null : stage / 10f,
        });
    }

    private bool TryUseArtisansKit(
        InventoryStack? held,
        ToolUseHand hand,
        VoxelWorldHit hit)
    {
        if (_player is not { IsMouseCaptured: true } player)
            return false;

        var (from, to) = player.GetInteractionRay(InteractionDistance);
        return _sessions.Active.ArtisansKit.TryEdit(
            held, hand, hit,
            new NVector3(from.X, from.Y, from.Z),
            new NVector3(to.X - from.X, to.Y - from.Y, to.Z - from.Z),
            player.CollisionBounds);
    }

    private void RotateHeldBlock()
    {
        if (_sessionStates.Player.CanInteract &&
            _sessions.Active.ArtisansKit.IsEquipped(
                _sessionStates.Player.Inventory.SelectedStack))
        {
            _sessions.Active.ArtisansKit.CycleResolution();
            SendArtisansKitState();
            return;
        }

        if (_sessionStates.Player.CanInteract &&
            _sessionStates.Player.HeldBlock.Rotate())
        {
            if (_player is not null)
            {
                SendTargetHudState(force: true);
            }
            SendWebUi(
                "game.held_block",
                new
                {
                    orientation = _sessionStates.Player.HeldBlock.Orientation.ToString(),
                    facing = _sessionStates.Player.HeldBlock.Facing.ToString(),
                    canRotate = _sessionStates.Player.HeldBlock.CanRotate,
                });
        }
    }

    private void PlaceTargetBlock()
    {
        if (!_sessionStates.Player.CanInteract || _player is null)
            return;

        var inventory = _sessionStates.Player.Inventory;
        var selected = inventory.SelectedStack;
        if (_sessions.Active.Tools.OpensBrushPalette(selected))
        {
            OpenBrushPalette();
            return;
        }

        if (_sessions.Active.ArchitectsCompass.IsEquipped(selected))
        {
            UseArchitectsCompass(selected, inventory.SelectedSlot, CurrentTarget());
            return;
        }

        var target = CurrentTarget();
        if (selected?.Kind == InventoryEntryKind.Layer)
        {
            if (target is { } layerHit &&
                _sessions.Active.LayerPlacement.TryPlace(selected, layerHit))
            {
                if (_sessionStates.Player.GameMode == PlayerGameMode.Survival)
                {
                    if (!inventory.TryConsumeSelected())
                        throw new InvalidOperationException("Accepted layer placement lost selected item.");
                    SyncHeldBlock();
                    SendHotbarState();
                    SendInventoryState();
                }
                KickWorldMutationWorkers();
            }
            return;
        }

        if (target is { } pickupHit)
        {
            var targetDefinition = _blocks.GetDefinition(
                _world.GetCellOrEmpty(pickupHit.Voxel).Block);
            if (targetDefinition.Interaction == BlockInteractionKind.Pickup)
            {
                if (!_inventoryCatalog.TryResolve(
                        InventoryEntryKind.Item,
                        targetDefinition.PickupItemId!,
                        null, null, out var pickupItem) || pickupItem is null)
                {
                    throw new InvalidOperationException(
                        $"Unresolved authored pickup item: {targetDefinition.PickupItemId}");
                }

                if (_blockInteractions.Pickup(pickupHit, inventory, pickupItem) ==
                    BlockPickupResult.Collected)
                {
                    SyncHeldBlock();
                    SendHotbarState();
                    SendInventoryState();
                    SendTargetHudState(force: true);
                    KickWorldMutationWorkers();
                }
                return;
            }
        }

        if (_sessions.Active.Bucket.IsEquipped(selected))
        {
            var (from, to) = _player.GetInteractionRay(
                BucketGameplayRuntime.MaximumReach);
            var origin = new NVector3(from.X, from.Y, from.Z);
            var direction = new NVector3(
                to.X - from.X, to.Y - from.Y, to.Z - from.Z);
            if (_sessions.Active.Bucket.TryUse(
                    inventory, origin, direction, target))
            {
                SendHotbarState();
                SendInventoryState();
                KickWorldMutationWorkers();
            }
            return;
        }

        if (target is not { } hit)
            return;

        if (_sessions.Active.ArtisansKit.IsEquipped(selected))
        {
            if (TryUseArtisansKit(selected, ToolUseHand.Right, hit))
                KickWorldMutationWorkers();
            return;
        }

        if (_sessions.Active.Tools.TryUse(
                selected, ToolUseHand.Right, hit))
        {
            KickWorldMutationWorkers();
            return;
        }
        if (selected?.Block is not { } block ||
            block.HasMicroblockGeometry) return;

        var decision =
            _blockInteractions.Place(
                hit,
                _sessionStates.Player.HeldBlock.CurrentCell(),
                _player!.CollisionBounds);

        if (!decision.Accepted)
        {
            return;
        }

        if (_sessionStates.Player.GameMode == PlayerGameMode.Survival)
        {
            if (!inventory.TryConsumeSelected())
                throw new InvalidOperationException(
                    "A selected stack was lost after accepted placement.");
            SyncHeldBlock();
            SendHotbarState();
            SendInventoryState();
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

    private void UseArchitectsCompass(
        InventoryStack selected, int slot, VoxelWorldHit? target)
    {
        if (target is not { } hit) return;
        var compass = _sessions.Active.ArchitectsCompass;
        var exportId = "asteria:structure_" + Guid.NewGuid().ToString("N");
        var result = compass.Select(selected, slot, hit, exportId,
            out var exported, out var message);
        if (result == ArchitectsCompassResult.Rejected)
            return;

        if (result == ArchitectsCompassResult.Exported && exported is not null)
        {
            try
            {
                var dir = ProjectSettings.GlobalizePath("user://exports/structures");
                Directory.CreateDirectory(dir);
                var file = Path.Combine(dir, exportId.Split(':')[1] + ".json");
                File.WriteAllText(file, exported.Json);
                message += $" Saved to {file}";
                GD.Print("structure.export " + file);
            }
            catch (IOException error)
            {
                result = ArchitectsCompassResult.Failed;
                message = $"Structure export could not be saved: {error.Message}";
                GD.PushError(message);
            }
            catch (UnauthorizedAccessException error)
            {
                result = ArchitectsCompassResult.Failed;
                message = $"Structure export permission denied: {error.Message}";
                GD.PushError(message);
            }
        }

        SendWebUi("game.hud.toast", new
        {
            message,
            tone = result == ArchitectsCompassResult.Failed ? "warning" :
                result == ArchitectsCompassResult.Exported ? "success" : "info",
            durationMs = 6500
        });
        SyncArchitectsCompassPreview();
    }

    private void SyncArchitectsCompassPreview()
    {
        var compass = _sessions.Active.ArchitectsCompass;
        if (compass.SelectionStart is null || !_sessionStates.Player.CanInteract ||
            _player is null || _inventoryOpen || _brushPaletteOpen)
        {
            _sessions.Active.StructureSelection.Hide();
            return;
        }
        _sessions.Active.StructureSelection.Sync(
            compass.TryPreviewBounds(CurrentTarget(), out var bounds)
                ? bounds : null);
    }

    private VoxelWorldHit? CurrentTarget()
    {
        if (_player is null)
        {
            return null;
        }

        var (from, to) =
            _player.GetInteractionRay(
                InteractionDistance);
        var direction = to - from;

        return VoxelWorldRaycaster.Raycast(
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
    }

    private bool TryGetTarget(
        out VoxelWorldHit hit)
    {
        var resolved =
            CurrentTarget();

        if (resolved is null)
        {
            hit = default;
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

        _worldDiagnostics?.ObserveTerrainMesh(
            completed.WorkerMilliseconds, completed.Accepted, completed.Stale);
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

        _worldDiagnostics?.ObserveLighting(
            report.WorkerMilliseconds, report.ProcessedVoxelCount);
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
