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
    private const int RetentionMarginChunks = 4;
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
    private PackSelectionStore _packSettings = null!;
    private ModLoader _mods = null!;
    private ClientPreferencesController _clientSettings = null!;
    private WorldCatalogScanController _worldCatalog = null!;
    private readonly WorldThumbnailCapture _worldThumbnail = new();
    private WorldLoadController _worldLoad = null!;
    private bool _clientInitialized;
    private WorldDeleteController _worldDelete = null!;
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
    private PackContentRegistry<ObjectDefinition> _objects = null!;
    private InventoryCraftingRuntime _crafting = null!;
    private InventoryContentCatalog _inventoryCatalog = null!;
    private PlayerVisualDefinition _playerVisual = null!;
    private HeldVisualResolver _heldVisuals = null!;
    private InventoryCategoryRegistry _inventoryCategories = null!;
    private InventoryDropIconCatalog _inventoryDropIcons = null!;
    private InventoryBlockPreviewRenderer _blockPreviewRenderer = null!;
    private InventoryVoxelIconRasterizer _voxelIcons = null!;
    private BlockPlacementPreviewPresentation _placementPreview = null!;
    private ObjectPlacementPreviewPresentation _objectPlacementPreview = null!;
    private CreaturePortraitPresentation? _creaturePortrait;
    private readonly Dictionary<string, string> _inventoryIconCache =
        new(StringComparer.Ordinal);
    private PackContentRegistry<CreatureDefinition> _creatures = null!;
    private PackContentRegistry<AttackDefinition> _attacks = null!;
    private (ulong Id, float Health, float Maximum, bool HasPortrait)? _lastCreatureHud;
    private TargetObjectHudSnapshot? _lastObjectHud;
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
    private PlayerPortraitPresentation? _playerPortrait;
    private PlayerModelPresentation? _playerModel;
    private ulong _publishedPlayerHealthRevision = ulong.MaxValue;
    private int _publishedOxygenSeconds = -1;
    private bool _publishedOxygenVisible;
    private bool _respawnArrival;
    private Node _webUi = null!;
    private TerrainTextureCatalog _terrainTextures = null!;
    private TerrainTextureLookup _terrainTextureLookup = null!;
    private VoxelTerrainMaterialSet _terrainMaterials = null!;
    private HeldDynamicLightPresentation _heldDynamicLight = null!;
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
    private int _nextWorldWorker;
    private bool _worldReadySent;
    private bool _inventoryOpen;
    // WRY and Godot can observe the same physical inventory shortcut.
    private bool _inventoryShortcutHeld;
    private ulong? _publishedStorageRevision;
    private bool _quitRequested;
    private bool _exitConfirmationPending;
    private readonly PlayerChatSession _chat = new();
    private WarpArrivalPlan? _warpArrival;
    private bool _portalTravelLatched;

    private enum DimensionTravelPurpose : byte
    {
        Ordinary,
        ChatWarp,
        Portal,
    }
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
        try
        {
            InitializeClient();
            _clientInitialized = true;
        }
        catch (Exception exception)
        {
            // An incomplete Godot _Ready() can still receive frame/input calls.
            // Report the original startup failure and stop the uninitialized
            // Main node instead of emitting secondary NullReferenceExceptions.
            _worldDiagnostics?.Write($"client.initialization.failed error={exception}");
            GD.PushError($"client.initialization.failed: {exception}");
            SetProcess(false);
            SetProcessInput(false);
            throw;
        }
    }

    private void InitializeClient()
    {
        // Window close and Alt+F4 must go through Save and Leave, not an
        // immediate engine shutdown that bypasses the session checkpoint.
        GetTree().AutoAcceptQuit = false;
        _worldDiagnostics = WorldDiagnosticsLog.Open();
        _worldDiagnostics.Write("client.initializing");
        _clientSettings = new ClientPreferencesController(
            ClientPreferencesStore.FromUserDataDirectory());
        _worldCatalog = new WorldCatalogScanController(WorldSaveDirectories.Root);
        GD.Print($"worlds directory: {WorldSaveDirectories.Root}");
        _keybindCapture = new KeybindCaptureController(
            _clientSettings);
        _packSettings = new PackSelectionStore(OS.GetUserDataDir());
        _packSelection = _packSettings.Current;
        _mods = new ModLoader(GameDirectories.Mods);
        GD.Print($"mods directory: {GameDirectories.Mods}");
        _mods.LoadEnabled();
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
        _objects = ObjectContentLoader.LoadProjectObjects(_packSelection);
        _inventoryCategories =
            InventoryCategoryContentLoader.LoadProjectCategories(_packSelection);
        _playerVisual = PlayerVisualContentLoader.Load(_packSelection);
        _heldVisuals = new HeldVisualResolver(_blocks, _items, _tools, _layers, _objects);
        _inventoryCatalog = new InventoryContentCatalog(
            _blocks, _items, _tools, _layers, _inventoryCategories, _objects);
        _crafting = new InventoryCraftingRuntime(
            CraftingContentLoader.LoadProjectRecipes(_packSelection),
            _inventoryCatalog);
        _inventoryDropIcons = new InventoryDropIconCatalog(
            _packSelection, _inventoryCatalog);
        _blockPreviewRenderer = new InventoryBlockPreviewRenderer(_packSelection);
        _creatures =
            CreatureContentLoader.LoadProjectCreatures(
                _packSelection);
        _creaturePortrait = new CreaturePortraitPresentation(
            _packSelection, _creatures);
        AddChild(_creaturePortrait);
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
        _worldCatalog.ConfigureValidation(
            _dimensions, _blocks, _fluids, _dyes, _layers);
        _worldLoad = new WorldLoadController(
            WorldSaveDirectories.Root,
            _dimensions, _blocks, _fluids, _dyes, _layers);
        _worldDelete = new WorldDeleteController(
            WorldSaveDirectories.Root);
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
        _voxelIcons = new InventoryVoxelIconRasterizer(
            _blocks, _fluids, _layers, _dyes, _terrainTextureLookup,
            _terrainTextures);
        _terrainMaterials =
            VoxelTerrainMaterialSet.Create(
                _terrainTextures);
        _heldDynamicLight = new HeldDynamicLightPresentation(
            this, _blocks, _objects);
        _placementPreview = new BlockPlacementPreviewPresentation(
            this, _blocks, _fluids, _terrainTextureLookup, _terrainMaterials);
        _objectPlacementPreview = new ObjectPlacementPreviewPresentation(
            this, _packSelection);
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
        GD.Print($"object content: loaded {_objects.Count} definitions");
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
        _worldDiagnostics?.Write(
            $"client.ready pack={_packSelection.Name} " +
            $"blocks={_blocks.AuthoredCount} fluids={_fluids.AuthoredCount} " +
            $"items={_items.Count} tools={_tools.Count} " +
            $"biomes={_biomes.Count} structures={_structures.Count} " +
            $"dimensions={_dimensions.Count}");
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest)
            RequestAppExit();
        else if (what == NotificationWMWindowFocusIn && _chat.IsOpen)
        {
            // A WRY child window can regain keyboard focus after the
            // original mount-time focus budget has expired (Alt+Tab).
            // Repeat the native handoff only while chat is still active.
            CallDeferred(nameof(FocusChatInput));
        }
        else if (what == NotificationWMWindowFocusOut && _worldReadySent &&
                 _worldSeed is not null && _player is not null &&
                 !_sessionStates.Player.Health.IsDead)
        {
            _inventoryShortcutHeld = false;
            // Windows may release native pointer capture before Godot
            // delivers focus-out. Always publish pause intent for gameplay,
            // even if IsMouseCaptured is already false. WebUI preserves any
            // existing modal; this never requests mouse recapture.
            _player.PauseGameplay();
            if (!_chat.IsOpen && !_inventoryOpen && !_brushPaletteOpen &&
                !_keybindCapture.IsCapturing &&
                _sessions.Active.ActiveStorageBox is null)
                SendWebUi("game.ui.pause", new { });
        }
    }

    public override void _ExitTree()
    {
        _worldDiagnostics?.Write("client.exiting");
        _worldDiagnostics?.Dispose();
        _worldDiagnostics = null;
    }

    public override void _Input(InputEvent @event)
    {
        if (!_clientInitialized)
            return;

        if (_keybindCapture.IsCapturing)
        {
            if (@event is InputEventKey captureKey &&
                captureKey.Pressed && !captureKey.Echo)
            {
                var action = _keybindCapture.PendingAction;
                var result = _keybindCapture.Press(captureKey);
                if (!_keybindCapture.IsCapturing)
                {
                    _webUi.Call("set_native_key_capture", false);
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

        // WRY may emit a semantic UI close and forward the same key to
        // Godot. Each physical press can toggle the inventory only once.
        if (@event is InputEventKey inventoryKey &&
            GameplayKeyMap.Matches(
                inventoryKey, _clientPreferences, KeybindAction.Inventory))
        {
            if (!inventoryKey.Pressed)
                _inventoryShortcutHeld = false;
            else if (!inventoryKey.Echo)
            {
                if (_inventoryShortcutHeld)
                {
                    GetViewport().SetInputAsHandled();
                    return;
                }
                _inventoryShortcutHeld = true;
            }
        }

        if (_sessionStates.Player.Health.IsDead)
            return;

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

        if (_sessions.Active.ActiveStorageBox is not null)
        {
            if (@event is InputEventKey storageKey &&
                storageKey.Pressed && !storageKey.Echo &&
                (storageKey.Keycode == Key.Escape ||
                 GameplayKeyMap.Matches(storageKey, _clientPreferences, KeybindAction.Inventory)))
            {
                CloseStorageBox();
                GetViewport().SetInputAsHandled();
            }
            return;
        }

        if (@event is not InputEventKey keyEvent ||
            !keyEvent.Pressed ||
            keyEvent.Echo)
        {
            return;
        }

        // Route Escape exactly once. Releasing mouse capture already emits
        // game.mouse_capture(false), which opens pause in WebUI. Forwarding
        // that SAME keypress as game.ui.escape would close pause immediately.
        // The player does not handle Escape independently.
        if (keyEvent.Keycode == Key.Escape)
        {
            if (_player is { IsMouseCaptured: true } capturedPlayer)
                capturedPlayer.PauseGameplay();
            else
                SendWebUi("game.ui.escape", new { });
            GetViewport().SetInputAsHandled();
            return;
        }

        // UI visibility hotkeys belong to the native composition root, not
        // FpsPlayer's mouse-captured movement path. WRY can temporarily return
        // mouse focus while still forwarding these key events to Godot.
        if (_worldReadySent && !_inventoryOpen && !_brushPaletteOpen &&
            !_chat.IsOpen && !_sessions.IsTransitioning)
        {
            if (GameplayKeyMap.Matches(
                    keyEvent, _clientPreferences, KeybindAction.Inventory))
            {
                OpenInventory();
                GetViewport().SetInputAsHandled();
                return;
            }

            if (GameplayKeyMap.Matches(
                    keyEvent, _clientPreferences, KeybindAction.Chat))
            {
                OpenChat();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

        if (_worldReadySent && !_inventoryOpen && !_brushPaletteOpen &&
            !_chat.IsOpen && !_sessions.IsTransitioning &&
            _player is { } activePlayer)
        {
            if (GameplayKeyMap.Matches(
                    keyEvent, _clientPreferences,
                    KeybindAction.ChangePerspective))
            {
                activePlayer.CycleCameraPerspective();
                GetViewport().SetInputAsHandled();
                return;
            }
        }

    }

    public override void _Process(double delta)
    {
        if (!_clientInitialized)
            return;

        _worldDiagnostics?.ObserveFrame(delta);
        PollWorldCatalog();
        PollWorldThumbnail();
        PollWorldLoad();
        PollWorldDelete();
        PollPlayerPortrait();
        PollChatLocate();
        AdvanceChatPresentation(delta);
        if (_worldSeed is null)
        {
            _placementPreview.Hide();
            _objectPlacementPreview.Hide();
            _worldDiagnostics?.FlushIfDue(0, 0, 0, 0, "MainMenu", 0, 0, 0, false);
            return;
        }

        BeginWorldFrameBudget(delta);
        if (_quitRequested && _worldReadySent && !_sessions.IsTransitioning &&
            !TrySaveAndLeaveCurrentWorld())
        {
            GD.PushWarning("world.save.failed: Could not begin save before exit.");
            SendWebUi("game.world.save_result", new { status = "error" });
            OfferExitWithoutSave();
        }
        AdvanceFpsHudState(delta);
        if (!_sessions.IsTransitioning && _worldReadySent)
            _sessionStates.Player.Hazards.AdvanceTime((float)Math.Clamp(delta, 0d, 1d));

        if (_sessions.IsTransitioning)
        {
            _placementPreview.Hide();
            _objectPlacementPreview.Hide();
            _heldDynamicLight.Clear();
            AdvanceDimensionTransition();
            _worldDiagnostics?.FlushIfDue(0, 0, 0, 0, "Transitioning", 0, 0, 0, false);
            return;
        }

        _sessions.Active.AdvanceWorldTime(
            delta, _player?.GlobalPosition, _worldReadySent);
        UpdateDayNightPresentation(delta);

        var loadingWorld =
            _loading.Progress.Phase is
                WorldLoadingPhase.MaterializingInitialArea or
                WorldLoadingPhase.PreparingPresentation or
                WorldLoadingPhase.PreparingArrivalStructure or
                WorldLoadingPhase.Ready;
        var streamingCenter =
            loadingWorld
                ? _loading.Center
                : CurrentStreamingCenter();
        var streamingRadius =
            loadingWorld
                ? WorldLoadingState.InitialHorizontalRadiusChunks
                : _clientPreferences.RenderDistanceChunks;

        var streamingPressure = WorldStreamingWorkPressure.Evaluate(
            delta, _worldUpdates.DirtyChunkCount, loadingWorld);
        var stageStart = Stopwatch.GetTimestamp();
        var streamingBegin =
            _chunkStreaming.BeginFrame(
                streamingCenter,
                _worldFrameBudget,
                streamingRadius,
                RequiresPortalSurfaceAccess,
                streamingPressure);
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.StreamingBegin, stageStart);
        ReportStreamingSelection(
            streamingBegin.Selection);
        ReportResidencyUpdate(
            streamingBegin.Collected);
        ReportResidencyUpdate(
            streamingBegin.Dispatched);
        ReportPresentationReservations(
            streamingBegin.ReservedPresentations);
        _worldDiagnostics?.ObserveStageMilliseconds(
            WorldFrameStage.CollisionActivation,
            _chunkPresentations.TakeCollisionActivationMilliseconds());

        var collisionPrewarmStart = Stopwatch.GetTimestamp();
        _chunkPresentations.PrewarmCollisionShapes(
            _residency, maximumShapes: 2, _worldFrameBudget);
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.CollisionPrewarm, collisionPrewarmStart);
        _worldDiagnostics?.ObserveDeferredCollisions(
            _chunkPresentations.DeferredCollisionChunkCount);

        var gameplaySliceStart = Stopwatch.GetTimestamp();
        var blockFrame = _blockEntities.Advance(
            delta, _dimension.GravityStrength);
        ReportBlockEntityFrame(blockFrame);
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.BlockEntities, gameplaySliceStart);
        if (_worldDiagnostics is { } diagnostics)
        {
            var timings = blockFrame.Timings;
            diagnostics.ObserveStageMilliseconds(
                WorldFrameStage.BlockPhysicsWake, timings.WakeMilliseconds);
            diagnostics.ObserveStageMilliseconds(
                WorldFrameStage.FallingSimulation, timings.FallingMilliseconds);
            diagnostics.ObserveStageMilliseconds(
                WorldFrameStage.DroppedSimulation, timings.DroppedMilliseconds);
            diagnostics.ObserveStageMilliseconds(
                WorldFrameStage.BlockEntityPresentation, timings.PresentationMilliseconds);
        }

        gameplaySliceStart = Stopwatch.GetTimestamp();
        AdvanceItemPickup(delta);
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.ItemPickup, gameplaySliceStart);

        stageStart = Stopwatch.GetTimestamp();
        PollFluidWorker();
        PollFluidMeshWorker();
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.FluidPoll, stageStart);

        stageStart = Stopwatch.GetTimestamp();
        IntegrateFluidMeshletPublications();
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.FluidPublication, stageStart);

        stageStart = Stopwatch.GetTimestamp();
        PollTerrainMeshWorker();
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.TerrainPoll, stageStart);

        stageStart = Stopwatch.GetTimestamp();
        IntegrateMeshletPublications();
        _chunkPresentations.IntegrateObjectChanges(4, _worldFrameBudget);
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.TerrainPublication, stageStart);

        stageStart = Stopwatch.GetTimestamp();
        PollLightingWorker();
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.LightingPoll, stageStart);

        stageStart = Stopwatch.GetTimestamp();
        DispatchOneWorldWorker(streamingPressure.IsPressured, streamingCenter);
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.WorkerDispatch, stageStart);

        stageStart = Stopwatch.GetTimestamp();
        var streamingEnd =
            _chunkStreaming.EndFrame(
                _worldFrameBudget);
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.StreamingEnd, stageStart);
        ReportRetirements(
            streamingEnd.Retirements);
        PublishStorageBoxChanges();
        _worldDiagnostics?.FlushIfDue(
            _world.ChunkCount,
            _residency.PendingCount,
            _residency.MaterializingCount,
            _chunkPresentations.Count,
            _loading.Progress.Phase.ToString(),
            _worldUpdates.LightingEditCount,
            _worldUpdates.DirtyChunkCount,
            _fluidMeshUpdates.DirtyChunkCount,
            _lightingRuntime.IsRunning);

        if (loadingWorld)
        {
            AdvanceWorldLoading();
            return;
        }

        stageStart = Stopwatch.GetTimestamp();
        gameplaySliceStart = Stopwatch.GetTimestamp();
        var miningFeedback = AdvanceSurvivalMining();
        _sessions.Active.MiningCracks.Sync(_world, _sessions.Active.Mining);
        _sessions.Active.SyncMiningFeedback((float)delta, miningFeedback);
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.Mining, gameplaySliceStart);

        if (_worldReadySent && _player is { } creatureObserver)
        {
            var position = creatureObserver.GlobalPosition;
            var playerTargetable =
                _sessionStates.Player.GameMode == PlayerGameMode.Survival &&
                !_sessionStates.Player.Health.IsDead &&
                _sessionStates.GameRules.AllowsCreatureAttacks;
            gameplaySliceStart = Stopwatch.GetTimestamp();
            var creatureTimings = _sessions.Active.AdvanceCreatures(
                delta,
                new NVector3(position.X, position.Y, position.Z),
                creatureObserver.CollisionBounds,
                playerTargetable);
            _worldDiagnostics?.ObserveStage(
                WorldFrameStage.Creatures, gameplaySliceStart);
            if (_worldDiagnostics is { } creatureDiagnostics)
            {
                creatureDiagnostics.ObserveStageMilliseconds(
                    WorldFrameStage.CreatureSpawn, creatureTimings.SpawnMilliseconds);
                creatureDiagnostics.ObserveStageMilliseconds(
                    WorldFrameStage.CreatureSimulation, creatureTimings.SimulationMilliseconds);
                creatureDiagnostics.ObserveStageMilliseconds(
                    WorldFrameStage.CreaturePresentation, creatureTimings.PresentationMilliseconds);
                creatureDiagnostics.ObserveStageMilliseconds(
                    WorldFrameStage.CreatureParticles, creatureTimings.ParticlesMilliseconds);
            }

            gameplaySliceStart = Stopwatch.GetTimestamp();
            ApplyCreatureMeleeAttacks();
            _worldDiagnostics?.ObserveStage(
                WorldFrameStage.CreatureAttacks, gameplaySliceStart);

            gameplaySliceStart = Stopwatch.GetTimestamp();
            var impacts = _sessions.Active.AdvanceProjectiles(
                delta, creatureObserver.CollisionBounds, playerTargetable);
            foreach (var impact in impacts)
            {
                if (impact.Target != ProjectileHitTarget.Player ||
                    impact.Damage <= 0f)
                    continue;
                var protection = PlayerEquipmentProtection.Calculate(
                    _sessionStates.Player.Inventory, _items);
                var damage = _sessionStates.Player.Hazards.ReceiveCreatureAttack(
                    impact.Damage, protection, _sessionStates.Player.Health,
                    _sessionStates.Player.GameMode, out var attackAccepted);
                if (attackAccepted && damage != PlayerDamageResult.Killed)
                {
                    var player = creatureObserver.GlobalPosition;
                    creatureObserver.ApplyCreatureImpact(
                        PlayerImpactImpulse.FromProjectile(
                            impact.Position,
                            new NVector3(player.X, player.Y, player.Z)));
                }
                if (attackAccepted &&
                    PlayerEquipmentWear.ApplyContactHit(
                        _sessionStates.Player.Inventory, _items) &&
                    _inventoryOpen)
                {
                    SendInventoryState();
                    _playerPortrait?.RequestCapture();
                }
                ApplyPlayerDamageResult(damage);
            }
            _worldDiagnostics?.ObserveStage(
                WorldFrameStage.Projectiles, gameplaySliceStart);
        }

        if (_worldReadySent && _player is { } particleObserver)
        {
            var cameraPosition = particleObserver.Camera.GlobalPosition;
            gameplaySliceStart = Stopwatch.GetTimestamp();
            _ambientParticleRuntime!.Advance(
                (float)delta,
                new NVector3(cameraPosition.X, cameraPosition.Y, cameraPosition.Z),
                _dimension.Id,
                _dimension.Environment.Wind,
                _world,
                _sessions.Active.Generator,
                _fluids);
            _worldDiagnostics?.ObserveStage(
                WorldFrameStage.AmbientSimulation, gameplaySliceStart);

            gameplaySliceStart = Stopwatch.GetTimestamp();
            _ambientParticlePresentation!.Sync(_ambientParticleRuntime);
            _worldDiagnostics?.ObserveStage(
                WorldFrameStage.AmbientPresentation, gameplaySliceStart);
        }

        _worldDiagnostics?.ObserveGameplayPopulation(
            _sessions.Active.Creatures.Count,
            _sessions.Active.Projectiles.Count,
            _ambientParticleRuntime?.Particles.Count ?? 0,
            _sessions.Active.CreatureParticleCount,
            _blockEntities.DroppedCount,
            _blockEntities.FallingCount,
            _blockEntities.PendingPhysicsUpdates,
            _blockEntities.WaitingPhysicsUpdates,
            _blockEntities.WaitingFallingCount);

        // When the HUD is hidden, skip its world queries, raycasts and
        // preview work entirely. This permits a genuine A/B streaming test.
        if (!_clientPreferences.HideHud)
        {
            gameplaySliceStart = Stopwatch.GetTimestamp();
            SendWorldHudState();
            SyncInventoryIconTint();
            SendWorldClockState();
            SendTargetHudState();
            SyncArchitectsCompassPreview();
            SyncPlacementPreview();
            _worldDiagnostics?.ObserveStage(
                WorldFrameStage.Hud, gameplaySliceStart);
        }

        gameplaySliceStart = Stopwatch.GetTimestamp();
        TryTraversePhysicalPortal();
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.Portals, gameplaySliceStart);
        _worldDiagnostics?.ObserveStage(
            WorldFrameStage.Gameplay, stageStart);
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
        NVector3 destination,
        DimensionTravelPurpose purpose = DimensionTravelPurpose.Ordinary)
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

        var sameSphere = target == _dimension.Id;
        if (!(sameSphere
                ? _sessions.RequestRelocation(source, destination)
                : _sessions.RequestTransition(
                    target, source, destination,
                    exactDestination: purpose == DimensionTravelPurpose.Portal)))
        {
            return false;
        }

        _warpArrival = purpose switch
        {
            DimensionTravelPurpose.ChatWarp =>
                new WarpArrivalPlan(_dimension.Id, source),
            DimensionTravelPurpose.Portal =>
                new WarpArrivalPlan(_dimension.Id, source, RequireExactDestination: true),
            _ => null,
        };
        StartDimensionRetirement(target);
        return true;
    }

    private void RequestAppExit()
    {
        if (_quitRequested)
            return;

        if (_exitConfirmationPending)
        {
            // A second window-close request must not silently discard the world.
            _player?.PauseGameplay();
            SendWebUi("game.app.exit_confirmation", new { });
            return;
        }

        _quitRequested = true;
        if (_worldSeed is null)
            GetTree().Quit();
        // A running world exits after Save and Leave completes, or after
        // the player explicitly accepts exiting without a failed save.
    }

    private void OfferExitWithoutSave()
    {
        _quitRequested = false;
        _exitConfirmationPending = true;
        _player?.PauseGameplay();
        _worldDiagnostics?.Write("client.exit_without_save.offered");
        SendWebUi("game.app.exit_confirmation", new { });
    }

    private void ConfirmExitWithoutSave()
    {
        if (!_exitConfirmationPending)
            return;

        _exitConfirmationPending = false;
        _worldDiagnostics?.Write("client.exit_without_save.confirmed");
        GetTree().Quit();
    }

    private void CancelExitWithoutSave()
    {
        if (!_exitConfirmationPending)
            return;

        _exitConfirmationPending = false;
        _worldDiagnostics?.Write("client.exit_without_save.cancelled");
    }

    // World data is committed only when leaving a world or exiting the game.
    private bool TrySaveAndLeaveCurrentWorld()
    {
        if (!_worldReadySent || _player is null ||
            _worldSeed is null || _sessions.IsTransitioning ||
            (_keybindCapture.IsCapturing && !_quitRequested))
            return false;

        var position = _player.GlobalPosition;
        var source = new NVector3(
            position.X, position.Y, position.Z);
        var directory = WorldSaveDirectories.ForWorld(_sessionStates.Name);
        var accepted = _sessions.RequestSaveAndLeave(
            directory, source, _blocks, _fluids, _dyes, _layers);
        if (!accepted)
            return false;

        // Capture while the gameplay camera still exists, before retirement.
        // Thumbnail failure never changes checkpoint acceptance or save data.
        _worldThumbnail.Capture(GetViewport());
        _worldDiagnostics?.Write(
            $"world.save.begin name={_sessionStates.Name} dimension={_dimension.Id} disposition=leave");
        StartDimensionRetirement(_dimension.Id);
        return true;
    }

    private void StartDimensionRetirement(DimensionId target)
    {
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

        _worldDiagnostics?.Write(
            $"dimension.transition.begin from={_dimension.Id} to={target}");
        GD.Print(
            $"dimension.transition begin from={_dimension.Id} to={target}");
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

        if (completion.IsCheckpoint)
        {
            _worldThumbnail.Complete(
                WorldSaveDirectories.ForWorld(_sessionStates.Name),
                completion.SaveError is null && completion.SavedGeneration.HasValue);
        }

        if (completion.LeftWorld)
        {
            FinishLeavingWorld(completion);
            return;
        }

        ActivateCurrentDimensionPresentation();
        BeginWorldLoading();

        if (completion.IsCheckpoint)
        {
            if (completion.SaveError is { } error)
            {
                // An exit-time failure requires explicit permission to close
                // without saving. Ordinary save failures keep the world open.
                GD.PushWarning($"world.save.failed: {error}");
                _worldDiagnostics?.Write($"world.save.failed error={error}");
                SendWebUi("game.world.save_result", new
                {
                    status = "error",
                    error,
                });
                if (_quitRequested)
                    OfferExitWithoutSave();
            }
            else
            {
                _worldDiagnostics?.Write(
                    $"world.save.committed generation={completion.SavedGeneration}");
                SendWebUi("game.world.save_result", new
                {
                    status = "saved",
                    generation = completion.SavedGeneration,
                });
            }
        }

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

        _worldDiagnostics?.Write(
            $"dimension.transition.complete from={completion.From} to={completion.To} " +
            $"archived_dirty={completion.Archive.ArchivedDirty} " +
            $"archived_pristine={completion.Archive.ArchivedPristine}");
        GD.Print(
            $"dimension.transition complete from={completion.From} " +
            $"to={completion.To} archived_dirty={completion.Archive.ArchivedDirty} " +
            $"archived_pristine={completion.Archive.ArchivedPristine}");
    }

    private void FinishLeavingWorld(DimensionTransitionCompletion completion)
    {
        if (!completion.IsCheckpoint || completion.SaveError is not null ||
            !completion.SavedGeneration.HasValue)
            throw new InvalidOperationException("Cannot leave without a committed session.");

        _worldDiagnostics?.Write(
            $"world.leave.complete generation={completion.SavedGeneration}");
        _dimensionEnvironment.Clear();
        _heldDynamicLight.Clear();
        _placementPreview.Hide();
        _objectPlacementPreview.Hide();
        _ambientParticleRuntime = null;
        _ambientParticlePresentation = null;
        _chat.Reset();
        _chatFeedbackSeconds = 0;
        _warpArrival = null;
        _portalTravelLatched = false;
        _lastClockHud = null;
        _lastCreatureHud = null;
        _publishedStorageRevision = null;
        _pickupAccumulator = 0;
        _worldHud.Reset();
        ResetInventoryIconTint();
        _targetHud.Reset();
        _loading.Reset();
        _lastLoadingProgress = null;
        _worldReadySent = false;
        _sessions = null!;
        _sessionStates = null!;
        _worldSeed = null;
        if (_quitRequested)
        {
            _worldDiagnostics?.Write("client.exit.after_world_save");
            GetTree().Quit();
            return;
        }

        _webUi.Call("set_creation_mode", true);
        SendWebUi("game.world.left", new { });
        _suggestedWorldSeed = WorldCreationSeed.GenerateRandom();
        SendWorldCreationState();
        BeginWorldCatalogScan();
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
            _sessions.Active.DayNight,
            _packSelection);
        _lastClockHud = null;
        SendWorldClockState(force: true);
    }

    private DayNightCycleDefinition ActiveDayNightCycle() =>
        _dayNightCycles.Get(
            _dimension.DayNightCycleId ??
            throw new InvalidOperationException(
                $"Dimension {_dimension.Id} has no day-night cycle."));

    private void UpdateDayNightPresentation(double deltaSeconds)
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
                player.Camera.GlobalPosition, deltaSeconds);
        }
        _heldDynamicLight.Sync(
            _worldReadySent && _sessionStates.Player.CanInteract
                ? _sessionStates.Player.Inventory
                : null,
            _player?.Camera);
    }

    private void SendWorldClockState(
        bool force = false)
    {
        if (_clientPreferences.HideHud) return;
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
            _objects,
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
                MaxChunkEvictionsPerFrame),
            _mods.AllowBlockBreak);

    private void RetirePlayerForDimensionTransition()
    {
        _placementPreview.Hide();
        _objectPlacementPreview.Hide();
        if (_player is null)
        {
            return;
        }

        _player.BreakRequested -=
            BreakTargetBlock;
        _player.Landed -= OnPlayerLanded;
        _player.FluidExposureSampled -= OnPlayerFluidExposure;
        _player.PlaceRequested -=
            PlaceTargetBlock;
        _player.PickBlockRequested -= PickTargetBlock;
        _player.ToolActionRequested -=
            RotateHeldBlock;
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
        _sessions.Active.CloseStorageBox();
        SendStorageBoxState();
        _sessions.Active.Mining.Cancel();
        PublishMiningProgress();
        _player.QueueFree();
        _player = null;
        _playerModel = null;
        _playerPortrait?.QueueFree();
        _playerPortrait = null;
        _underwaterView = null;
        _worldHud.Reset();
        ResetInventoryIconTint();
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
            _worldDiagnostics?.Error("dimension.retirement.worker_failed", error);
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
        // _Ready can fail after connecting signals but before registries
        // and session controllers are initialized. Ignore late UI events.
        if (!_clientInitialized)
            return;

        SendCurrentState();
        if (_exitConfirmationPending)
            SendWebUi("game.app.exit_confirmation", new { });
    }

    private void OnWebUiMessage(string message)
    {
        if (!_clientInitialized)
            return;

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
                    _webUi.Call("set_native_key_capture", true);
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
                    _webUi.Call("set_native_key_capture", false);
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
                    if (type == "ui.client_preferences.hide_hud")
                    {
                        if (_clientPreferences.HideHud)
                        {
                            // Clear existing 3D previews immediately; they must
                            // not remain visible while their updates are paused.
                            _placementPreview.Hide();
                            _objectPlacementPreview.Hide();
                            if (_worldSeed is not null)
                                _sessions.Active.StructureSelection.Hide();
                        }
                        else if (_worldSeed is not null && _player is not null)
                        {
                            SendWorldHudState(force: true);
                            SendWorldClockState(force: true);
                            SendTargetHudState(force: true);
                            SendPlayerVitals(force: true);
                            SendFpsHudState();
                        }
                    }
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
                case "ui.mods.refresh":
                    if (_worldSeed is null) SendModsCatalog();
                    break;
                case "ui.mods.configure":
                    if (_worldSeed is not null) break;
                    try
                    {
                        if (!document.RootElement.TryGetProperty("payload", out var modPayload) ||
                            !modPayload.TryGetProperty("id", out var idProperty) ||
                            idProperty.ValueKind != JsonValueKind.String ||
                            !modPayload.TryGetProperty("enabled", out var enabledProperty) ||
                            enabledProperty.ValueKind is not (JsonValueKind.True or JsonValueKind.False) ||
                            !modPayload.TryGetProperty("settings", out var settingsProperty) ||
                            settingsProperty.ValueKind != JsonValueKind.Object)
                            throw new FormatException("Invalid mod settings request.");
                        _mods.Configure(idProperty.GetString()!, enabledProperty.GetBoolean(),
                            settingsProperty);
                        SendModsCatalog();
                    }
                    catch (Exception error) when (error is IOException or
                        ArgumentException or FormatException or InvalidDataException)
                    {
                        SendWebUi("game.mods.error", new { message = error.Message });
                    }
                    break;
                case "ui.packs.refresh":
                    if (_worldSeed is null) SendPackCatalog();
                    break;
                case "ui.packs.configure":
                    if (_worldSeed is not null) break;
                    try
                    {
                        if (!document.RootElement.TryGetProperty("payload", out var packPayload))
                            throw new FormatException("Missing pack payload.");
                        _packSettings.Configure(PackSelectionStore.ReadEntries(packPayload));
                        SendPackCatalog();
                    }
                    catch (Exception error) when (error is IOException or
                        ArgumentException or FormatException or InvalidDataException)
                    {
                        SendWebUi("game.packs.error", new { message = error.Message });
                    }
                    break;
                case "ui.ping":
                    SendWebUi(
                        "game.pong",
                        new { timestamp = Time.GetTicksMsec() });
                    break;
                case "ui.app.exit":
                    RequestAppExit();
                    break;
                case "ui.app.exit.confirm_without_save":
                    ConfirmExitWithoutSave();
                    break;
                case "ui.app.exit.cancel_without_save":
                    CancelExitWithoutSave();
                    break;
                case "ui.world.catalog.refresh":
                    if (_worldSeed is null && !_worldDelete.IsBusy)
                        BeginWorldCatalogScan();
                    break;
                case "ui.world.catalog.load":
                    if (_worldSeed is not null || _worldDelete.IsBusy ||
                        _worldLoad.IsBusy || _worldCatalog.IsBusy ||
                        !TryReadWorldCatalogId(document.RootElement, out var savedId))
                    {
                        SendWebUi("game.world_catalog.load_error",
                            new { error = "World load request is unavailable." });
                        break;
                    }
                    try
                    {
                        if (_worldLoad.Begin(savedId))
                            SendWebUi("game.world_catalog", new { status = "verifying" });
                        else
                            SendWebUi("game.world_catalog.load_error",
                                new { error = "A world load is already running." });
                    }
                    catch (ArgumentException error)
                    {
                        SendWebUi("game.world_catalog.load_error",
                            new { error = error.Message });
                    }
                    break;
                case "ui.world.catalog.recreate":
                    if (_worldSeed is not null || _worldDelete.IsBusy ||
                        _worldLoad.IsBusy || _worldCatalog.IsBusy ||
                        !TryReadWorldCatalogId(document.RootElement, out var recreateId))
                    {
                        SendWebUi("game.world_catalog.load_error",
                            new { error = "World recreation request is unavailable." });
                        break;
                    }
                    try
                    {
                        if (_worldLoad.BeginRecreation(recreateId))
                            SendWebUi("game.world_catalog", new { status = "verifying" });
                        else
                            SendWebUi("game.world_catalog.load_error",
                                new { error = "A world load is already running." });
                    }
                    catch (ArgumentException error)
                    {
                        SendWebUi("game.world_catalog.load_error",
                            new { error = error.Message });
                    }
                    break;
                case "ui.world.catalog.delete":
                    if (_worldSeed is not null || _worldDelete.IsBusy ||
                        _worldLoad.IsBusy || _worldCatalog.IsBusy ||
                        !TryReadWorldCatalogId(document.RootElement, out var deleteId))
                    {
                        SendWebUi("game.world_catalog.delete_result",
                            new { status = "error" });
                        break;
                    }

                    try
                    {
                        var id = deleteId;
                        if (_worldDelete.Begin(id))
                            SendWebUi("game.world_catalog.delete_result",
                                new { status = "deleting", id });
                    }
                    catch (ArgumentException error)
                    {
                        GD.PushWarning($"world.delete.rejected: {error.Message}");
                        SendWebUi("game.world_catalog.delete_result",
                            new { status = "error" });
                    }
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
                    _worldDiagnostics?.Write("world.creation.requested");
                    if (_worldSeed is null && !_worldDelete.IsBusy &&
                        !_worldLoad.IsBusy)
                    {
                        StartRequestedWorld(
                            document.RootElement);
                    }
                    else
                    {
                        _worldDiagnostics?.Write(
                            $"world.creation.ignored world_active={_worldSeed is not null} " +
                            $"delete_busy={_worldDelete.IsBusy} " +
                            $"load_busy={_worldLoad.IsBusy}");
                    }

                    break;
                case "ui.world.leave":
                    if (!TrySaveAndLeaveCurrentWorld())
                        SendWebUi("game.world.save_result", new { status = "error" });
                    break;
                case "ui.player.respawn":
                    TryRespawnPlayer();
                    break;
                case "ui.player.set_game_mode":
                    SetRequestedGameMode(document.RootElement);
                    break;
                case "ui.world.set_ticks":
                    SetRequestedWorldTicks(document.RootElement);
                    break;
                case "ui.world.set_spawn_creatures":
                    SetRequestedSpawnCreatures(document.RootElement);
                    break;
                case "ui.world.set_creature_attack_mode":
                    SetRequestedCreatureAttackMode(document.RootElement);
                    break;
                case "ui.world.set_keep_inventory":
                    SetRequestedKeepInventory(document.RootElement);
                    break;
                case "ui.game.resume":
                    if (_worldReadySent && !_keybindCapture.IsCapturing &&
                        !_inventoryOpen && !_chat.IsOpen &&
                        !_sessionStates.Player.Health.IsDead)
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
                case "ui.chat.action":
                    ExecuteChatMessageAction(document.RootElement);
                    break;
                case "ui.inventory.close":
                    if (document.RootElement.TryGetProperty("payload", out var closePayload) &&
                        closePayload.ValueKind == JsonValueKind.Object &&
                        closePayload.TryGetProperty("shortcut", out var shortcut) &&
                        shortcut.ValueKind == JsonValueKind.True)
                        _inventoryShortcutHeld = true;
                    CloseInventory();
                    break;
                case "ui.player.portrait.rotate":
                    RotatePlayerPortrait(document.RootElement);
                    break;
                case "ui.storage_box.close":
                    CloseStorageBox();
                    break;
                case "ui.storage_box.slot":
                    HandleStorageBoxSlot(document.RootElement);
                    break;
                case "ui.storage_box.sort":
                    if (_sessionStates.Player.CanInteract &&
                        _sessions.Active.ActiveStorageBox is not null &&
                        _sessions.Active.TrySortStorageBox())
                        SendStorageBoxState();
                    break;
                case "ui.brush.select":
                    SelectBrushDye(document.RootElement);
                    break;
                case "ui.brush.close":
                    CloseBrushPalette();
                    break;
                case "ui.inventory.equipment":
                    HandleEquipmentSlot(document.RootElement);
                    break;
                case "ui.inventory.slot":
                    HandleInventorySlot(document.RootElement);
                    break;
                case "ui.inventory.sort":
                    if (_sessionStates.Player.CanInteract &&
                        (_inventoryOpen || _sessions.Active.ActiveStorageBox is not null) &&
                        _sessionStates.Player.Inventory.SortBackpack())
                        SendInventoryState();
                    break;
                case "ui.inventory.discard_cursor":
                    if (_sessionStates.Player.CanInteract &&
                        _inventoryOpen && _sessionStates.Player.Inventory.DiscardCursor())
                        SendInventoryState();
                    break;
                case "ui.inventory.drop_cursor":
                    DropInventoryCursor();
                    break;
                case "ui.inventory.clear":
                    if (_sessionStates.Player.CanInteract && _inventoryOpen &&
                        _sessionStates.Player.Inventory.ClearBackpackAndHotbar())
                    {
                        SyncHeldBlock();
                        SendHotbarState();
                        SendInventoryState();
                        _playerPortrait?.RequestCapture();
                    }
                    break;
                case "ui.inventory.creative_pick":
                    HandleCreativePick(document.RootElement);
                    break;
                case "ui.inventory.craft":
                    HandleInventoryCraft(document.RootElement);
                    break;
            }
        }
        catch (JsonException exception)
        {
            _worldDiagnostics?.Warn("webui.invalid_message", exception.Message);
            GD.PushWarning(
                $"Ignoring invalid WebUI message: {exception.Message}");
        }
    }

    private void SendModsCatalog() =>
        SendWebUi("game.mods.catalog", _mods.Catalog());

    private void SendPackCatalog()
    {
        var selected = _packSettings.Current.Overlays;
        SendWebUi("game.packs.catalog", new
        {
            packs = _packSettings.Installed().Select(pack => new
            {
                name = pack.Folder,
                id = pack.Id,
                title = pack.Name,
                version = pack.Version,
                description = pack.Description,
                error = pack.Error,
                scope = selected.FirstOrDefault(layer => layer.Name == pack.Folder)
                    .Domains.ToString(),
                enabled = selected.Any(layer => layer.Name == pack.Folder),
                order = Array.FindIndex(selected.ToArray(), layer => layer.Name == pack.Folder),
            }).ToArray(),
            restartRequired = !_packSelection.Overlays.SequenceEqual(selected),
        });
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
        SendPackCatalog();
        SendModsCatalog();

        if (_worldSeed is null)
        {
            SendWorldCreationState();
            BeginWorldCatalogScan();
            return;
        }

        SendPlayerModeState();
        SendPlayerVitals(force: true);
        if (_sessionStates.Player.Health.IsDead)
            SendPlayerDeathState();
        SendWorldSettings();
        SendHotbarState();
        SendInventoryState();
        SendStorageBoxState();
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

    private void SetRequestedSpawnCreatures(JsonElement message)
    {
        if (_worldSeed is null ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("value", out var raw) ||
            raw.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            SendWebUi("game.world_settings.error", new { code = "InvalidValue" });
            return;
        }

        if (_sessionStates.GameRules.SetSpawnCreatures(raw.GetBoolean()))
        {
            SendWorldSettings();
        }
    }

    private void SetRequestedCreatureAttackMode(JsonElement message)
    {
        if (_worldSeed is null ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("value", out var raw) ||
            raw.ValueKind != JsonValueKind.String ||
            !Enum.TryParse<CreatureAttackMode>(raw.GetString(),
                ignoreCase: false, out var mode) ||
            !Enum.IsDefined(mode))
        {
            SendWebUi("game.world_settings.error", new { code = "InvalidValue" });
            return;
        }

        if (_sessionStates.GameRules.SetCreatureAttackMode(mode))
            SendWorldSettings();
    }

    private void SetRequestedKeepInventory(JsonElement message)
    {
        if (_worldSeed is null ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("value", out var raw) ||
            raw.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            SendWebUi("game.world_settings.error", new { code = "InvalidValue" });
            return;
        }
        if (_sessionStates.GameRules.SetKeepInventory(raw.GetBoolean()))
            SendWorldSettings();
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
                creatureAttackMode = _sessionStates.GameRules.AttackMode.ToString(),
                keepInventory = _sessionStates.GameRules.KeepInventory,
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

    private static bool TryReadWorldCatalogId(JsonElement message, out string id)
    {
        id = "";
        if (!message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("id", out var idValue) ||
            idValue.ValueKind != JsonValueKind.String)
            return false;

        id = idValue.GetString() ?? "";
        return !string.IsNullOrWhiteSpace(id);
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

    private void PollWorldThumbnail()
    {
        // One bounded asynchronous image read and one IPC message per frame.
        // A thumbnail is optional metadata, never evidence of restorable data.
        if (_worldSeed is not null ||
            !_worldCatalog.TryPollThumbnail(out var id, out var url, out var error))
            return;
        if (error is not null)
            GD.PushWarning($"world.thumbnail.read.failed id={id}: {error}");
        if (url is not null)
            SendWebUi("game.world_catalog.thumbnail", new { id, thumbnailUrl = url });
    }

    private void PollWorldDelete()
    {
        if (!_worldDelete.TryPoll(out var error))
            return;

        if (error is not null)
        {
            GD.PushWarning($"world.delete.failed: {error}");
            _worldDiagnostics?.Write($"world.delete.failed error={error}");
            SendWebUi("game.world_catalog.delete_result",
                new { status = "error" });
        }
        else
        {
            _worldDiagnostics?.Write("world.delete.succeeded");
            SendWebUi("game.world_catalog.delete_result",
                new { status = "deleted" });
        }

        if (_worldSeed is null)
            BeginWorldCatalogScan();
    }

    private void PollWorldLoad()
    {
        if (!_worldLoad.TryPoll(out var result, out var error) ||
            _worldSeed is not null)
            return;

        if (result is not { } prepared)
        {
            GD.PushWarning($"world.load.failed: {error}");
            SendWebUi("game.world_catalog.load_error",
                new { error = error ?? "Unable to restore the selected world." });
            BeginWorldCatalogScan();
            return;
        }

        var active = prepared.Snapshot.ActiveSphere ??
            throw new InvalidDataException("Restored world has no active Sphere.");
        if (prepared.Intent == WorldLoadIntent.Recreate)
        {
            var copy = prepared.States.CreateRecreationOptions();
            StartWorld(copy);
            _worldDiagnostics?.Write(
                $"world.recreated from={prepared.Snapshot.Name} name={copy.Name}");
            return;
        }

        InitializeWorldSession(prepared.States, active);
        _worldDiagnostics?.Write(
            $"world.load.restored name={prepared.Snapshot.Name} dimension={active}");
    }

    private void SendWorldCreationState()
    {
        var initialSphere = _dimensions.Get(
            new DimensionId(StartupDimensionId));
        var ocean = initialSphere.GeneratedOcean?.Biome;
        var spawnBiomes = initialSphere.SurfaceBiomes
            .Where(id => !string.Equals(id, ocean, StringComparison.Ordinal))
            .OrderBy(id => id, StringComparer.Ordinal)
            .Select(id => new
            {
                id,
                label = string.Join(" ", id.Split('/')[^1]
                    .Split('_')
                    .Select(part => part.Length > 0
                        ? char.ToUpperInvariant(part[0]) + part[1..]
                        : part)),
            })
            .ToArray();

        SendWebUi(
            "game.world_creation",
            new
            {
                seed = WorldCreationSeed.Format(_suggestedWorldSeed),
                spawnBiomes,
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

        var spawnCreatures = payload.TryGetProperty(
            "spawnCreatures", out var spawnCreaturesElement) &&
            spawnCreaturesElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? spawnCreaturesElement.GetBoolean()
                : true;
        var rawCreatureAttackMode = payload.TryGetProperty(
            "creatureAttackMode", out var attackModeElement)
            ? attackModeElement.ValueKind == JsonValueKind.String
                ? attackModeElement.GetString()
                : null
            : nameof(CreatureAttackMode.Default);
        if (!Enum.TryParse<CreatureAttackMode>(rawCreatureAttackMode,
                ignoreCase: false, out var creatureAttackMode) ||
            !Enum.IsDefined(creatureAttackMode))
        {
            SendWorldCreationError("newWorld.error.invalidCreatureAttackMode");
            return;
        }

        var keepInventory = payload.TryGetProperty(
            "keepInventory", out var keepInventoryElement) &&
            keepInventoryElement.ValueKind is JsonValueKind.True or JsonValueKind.False
                ? keepInventoryElement.GetBoolean()
                : true;

        try
        {
            var generation = WorldGenerationRequestParser.Parse(
                payload,
                _dimensions.Get(new DimensionId(StartupDimensionId)));
            var creation = new WorldCreationOptions(
                name,
                selectedSeed,
                mode,
                ticks,
                spawnCreatures,
                generation,
                keepInventory,
                creatureAttackMode);
            _worldDiagnostics?.Write(
                $"world.creation.accepted mode={generation.Mode} " +
                $"spawn_structures={generation.SpawnStructures} " +
                $"spawn_caves={generation.SpawnCaves} " +
                $"spawn_oceans={generation.SpawnOceans}");
            StartWorld(creation);
        }
        catch (ArgumentException exception)
        {
            _worldDiagnostics?.Warn("world.creation.rejected", exception.Message);
            SendWorldCreationError(exception.ParamName switch
            {
                "name" => "newWorld.error.invalidName",
                "ticksPerSecond" => "newWorld.error.invalidTickRate",
                _ => "newWorld.error.invalidGeneration",
            });
        }
        catch (Exception exception)
        {
            // Preserve the original failure, including its stack, in the
            // durable session log. A session-start exception must never
            // masquerade as a terrain-mode selection failure.
            _worldDiagnostics?.Write(
                $"world.creation.failed exception={exception}");
            throw;
        }
    }

    private void StartWorld(
        WorldCreationOptions creation)
    {
        if (_worldSeed is not null)
        {
            return;
        }

        var name = WorldSaveNameAllocator.Allocate(
            WorldSaveDirectories.Root, creation.Name);
        var safeCreation = name == creation.Name
            ? creation
            : new WorldCreationOptions(
                name, creation.Seed, creation.GameMode,
                creation.TicksPerSecond, creation.SpawnCreatures,
                creation.Generation, creation.KeepInventory,
                creation.AttackMode);

        InitializeWorldSession(
            new DimensionSessionStateStore(safeCreation, _dimensions),
            new DimensionId(StartupDimensionId));
    }

    private void InitializeWorldSession(
        DimensionSessionStateStore states,
        DimensionId startDimension)
    {
        // This method is the one native publication point for newly
        // created worlds and fully decoded/restored world sessions.
        _sessionStates = states;
        _sessions = new DimensionSessionController(
            _sessionStates, CreateDimensionSession);
        _sessions.Start(startDimension);
        ActivateCurrentDimensionPresentation();

        // Restoration retains the full authoritative inventory; starting a
        // new world supplies an empty one from DimensionSessionStateStore.
        SyncHeldBlock();
        _worldSeed = _sessionStates.WorldSeed;

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
                        _sessionStates.WorldSeed),
            });
        SendHotbarState();
        SendInventoryState();
        SendStorageBoxState();
        SendCreativeCatalog();

        _worldDiagnostics?.WorldStarted(
            _sessionStates.WorldSeed, _dimension.Id, _dimensionSeed);
        _worldDiagnostics?.Write(
            $"world.settings render_distance_chunks={_clientPreferences.RenderDistanceChunks} " +
            $"retention_margin_chunks={RetentionMarginChunks} " +
            $"max_materialization_workers={MaxMaterializationTasksInFlight}");
        GD.Print(
            $"world.start seed={_sessionStates.WorldSeed} " +
            $"dimension={_dimension.Id} " +
            $"dimension_seed={_dimensionSeed}");
        GD.Print(
            $"streaming: render_distance={_clientPreferences.RenderDistanceChunks} " +
            $"retention_margin={RetentionMarginChunks} " +
            $"materialization_in_flight={MaxMaterializationTasksInFlight}");

        BeginWorldLoading();
    }

    private bool RequiresPortalSurfaceAccess =>
        _warpArrival is { RequireExactDestination: true, IsReturning: false } &&
        _sessions.Active.Dimension.PortalArrivalStructure is not null;

    private void BeginWorldLoading()
    {
        _worldDiagnostics?.Write(
            $"world.loading.begin dimension={_dimension.Id} center={_spawnChunk}");
        _worldReadySent = false;
        _loading.Begin(
            _spawnChunk);
        _lastLoadingProgress = null;

        ReportStreamingSelection(
            _chunkStreaming.SyncSelection(
                _loading.Center,
                WorldLoadingState.InitialHorizontalRadiusChunks,
                RequiresPortalSurfaceAccess));
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

        if (_loading.Progress.Phase == WorldLoadingPhase.PreparingArrivalStructure)
            _loading.UpdateArrivalStructurePresentation(
                _sessions.Active.IsPortalArrivalPresentationCurrent());

        if (!_loading.Progress.IsReady)
        {
            SendLoadingState();
            return;
        }

        if (_warpArrival is { } warp)
        {
            if (warp.Phase == WarpArrivalPhase.AwaitingStructurePublication &&
                !_sessions.Active.IsPortalArrivalPresentationCurrent())
                return;

            var exactPortalEntry = warp.RequireExactDestination && !warp.IsReturning
                ? _sessions.Active.CheckResidentPortalEntry()
                : (ExactArrivalStatus?)null;
            var needsReturnPortal = exactPortalEntry is not null &&
                _sessions.Active.Dimension.PortalArrivalStructure is not null;
            var returnPortalReady = !needsReturnPortal ||
                _sessions.Active.HasNearbyReturnPortal(warp.Origin);
            // A safe foothold alone is insufficient: every new physical
            // crossing must produce a working return link. Underground
            // arrivals also need their authored surface connection.
            if (exactPortalEntry is { } entry &&
                warp.Phase == WarpArrivalPhase.Requested &&
                entry != ExactArrivalStatus.NonResident &&
                entry != ExactArrivalStatus.SessionRetiring &&
                (entry != ExactArrivalStatus.Safe || !returnPortalReady))
            {
                var placement = _sessions.Active.TryPreparePortalArrivalStructure(
                    warp.Origin);
                if (placement is { } result)
                {
                    _worldDiagnostics?.Write(
                        $"portal.arrival.structure dimension={_dimension.Id} " +
                        $"result={result}");
                    if (result == ManualStructurePlacementResult.Placed)
                    {
                        _warpArrival = warp.AwaitStructurePublication();
                        _loading.BeginArrivalStructurePresentation();
                        SendLoadingState();
                        return;
                    }
                }
            }
            returnPortalReady = !needsReturnPortal ||
                _sessions.Active.HasNearbyReturnPortal(warp.Origin);
            var residentEntrySafe = exactPortalEntry is { } status
                ? status == ExactArrivalStatus.Safe && returnPortalReady
                : _sessions.Active.TryPrepareResidentWarpEntry();
            if (exactPortalEntry is { } rejected && rejected != ExactArrivalStatus.Safe)
            {
                var at = _sessions.Active.InitialPlayerPosition;
                _worldDiagnostics?.Write(
                    $"portal.arrival.rejected dimension={_dimension.Id} " +
                    $"position={at} reason={rejected} return_portal={returnPortalReady}");
            }
            var disposition = warp.Evaluate(residentEntrySafe);
            switch (disposition)
            {
                case WarpArrivalDisposition.RetryAtGeneratedSpawn:
                    _warpArrival = warp.RetryAtGeneratedSpawn();
                    _sessions.Active.PrepareGeneratedSpawn();
                    BeginWorldLoading();
                    return;
                case WarpArrivalDisposition.ReturnToOrigin:
                    BeginWarpRollback(warp);
                    return;
                case WarpArrivalDisposition.NoSafeEntry:
                    // The player has not been instantiated: no valid
                    // resident destination or source exists.
                    throw new InvalidOperationException(
                        "Warp rollback has no safe resident arrival " +
                        $"in Sphere {_dimension.Id}.");
            }

            _warpArrival = null;
            var position = _sessions.Active.InitialPlayerPosition;
            if (warp.IsReturning)
            {
                ChatFeedback("chat.command.warp.failed", error: true);
            }
            else
            {
                ChatFeedback("chat.command.warp.success", error: false,
                    ("position",
                        $"({Mathf.FloorToInt(position.X)}, " +
                        $"{Mathf.FloorToInt(position.Z)}, " +
                        $"{Mathf.FloorToInt(position.Y)})"));
            }
            _chatFeedbackSeconds = 10.0;
            SendChatState();
        }

        _worldReadySent = true;
        _worldDiagnostics?.Write(
            $"world.loading.ready dimension={_dimension.Id} " +
            $"resident={_world.ChunkCount} presented={_chunkPresentations.Count}");
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

    private void BeginWarpRollback(WarpArrivalPlan warp)
    {
        // Keep the player retired. Reentry to the archived origin uses the
        // normal world/session transition and must validate residency again.
        var current = _sessions.Active.InitialPlayerPosition;
        var accepted = warp.Origin == _dimension.Id
            ? _sessions.RequestRelocation(current, warp.OriginPosition)
            : _sessions.RequestTransition(
                warp.Origin, current, warp.OriginPosition);
        if (!accepted)
        {
            throw new InvalidOperationException(
                "Failed to initiate rollback after unsafe warp arrival.");
        }

        _warpArrival = warp.BeginReturn();
        StartDimensionRetirement(warp.Origin);
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
                WorldLoadingPhase.PreparingPresentation or
                    WorldLoadingPhase.PreparingArrivalStructure =>
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

    private void SendPlayerVitals(bool force = false)
    {
        var health = _sessionStates.Player.Health;
        var breath = _sessionStates.Player.Hazards;
        var visible = breath.BreathDepleting &&
            _sessionStates.Player.GameMode == PlayerGameMode.Survival;
        var seconds = visible ? (int)MathF.Ceiling(breath.BreathSeconds) : -1;
        if (!force && _publishedPlayerHealthRevision == health.Revision &&
            _publishedOxygenSeconds == seconds &&
            _publishedOxygenVisible == visible)
            return;
        _publishedPlayerHealthRevision = health.Revision;
        _publishedOxygenSeconds = seconds;
        _publishedOxygenVisible = visible;
        SendWebUi("game.hud.vitals", new
        {
            health = new { current = health.Current, maximum = health.Maximum },
            oxygen = visible
                ? new { current = seconds, maximum = PlayerHazardRuntime.BreathCapacitySeconds }
                : null,
            stamina = (object?)null,
        });
    }

    private void OnPlayerLanded(float distance)
    {
        if (!_worldReadySent || _sessions.IsTransitioning)
            return;
        var result = _sessionStates.Player.Health.Land(
            distance, _sessionStates.Player.GameMode);
        ApplyPlayerDamageResult(result);
    }

    private void ApplyCreatureMeleeAttacks()
    {
        // Only explicit, one-shot slime attack impacts can damage a player.
        // Reading these hits drains them even if the player is not targetable.
        var attacks = _sessions.Active.Creatures.DrainMeleeHits();
        if (_player is null ||
            !_sessionStates.Player.CanInteract ||
            !_sessionStates.GameRules.AllowsCreatureAttacks)
            return;

        foreach (var hit in attacks)
        {
            var protection = PlayerEquipmentProtection.Calculate(
                _sessionStates.Player.Inventory, _items);
            var result = _sessionStates.Player.Hazards.ReceiveCreatureAttack(
                hit.Damage, protection, _sessionStates.Player.Health,
                _sessionStates.Player.GameMode, out var attackAccepted);
            if (attackAccepted && result != PlayerDamageResult.Killed)
            {
                var player = _player.GlobalPosition;
                _player.ApplyCreatureImpact(PlayerImpactImpulse.FromMelee(
                    hit.AttackerPosition,
                    new NVector3(player.X, player.Y, player.Z)));
            }
            if (attackAccepted &&
                PlayerEquipmentWear.ApplyContactHit(_sessionStates.Player.Inventory, _items) &&
                _inventoryOpen)
            {
                SendInventoryState();
                _playerPortrait?.RequestCapture();
            }
            ApplyPlayerDamageResult(result);
        }
    }

    private void OnPlayerFluidExposure(float delta, FluidBodyContact contact)
    {
        if (!_worldReadySent || _sessions.IsTransitioning) return;
        var definition = contact.Fluid == FluidRuntimeId.None
            ? null : _fluids.GetDefinition(contact.Fluid);
        var result = _sessionStates.Player.Hazards.AdvanceFluid(
            delta, contact.EyeSubmerged, contact.IsImmersed,
            definition?.DepletesBreath ?? false,
            definition?.ContactDamagePerSecond ?? 0f,
            _sessionStates.Player.Health,
            _sessionStates.Player.GameMode);
        ApplyPlayerDamageResult(result);
        SendPlayerVitals();
    }

    private void ApplyPlayerDamageResult(PlayerDamageResult result)
    {
        if (result == PlayerDamageResult.Ignored) return;

        _playerModel?.TryPlayAction(result == PlayerDamageResult.Killed
            ? PlayerVisualAction.Death : PlayerVisualAction.Hurt);
        SendPlayerVitals();
        if (result == PlayerDamageResult.Killed)
        {
            var position = _player?.GlobalPosition ?? Vector3.Zero;
            var dropPosition = new NVector3(
                position.X, MathF.Max(position.Y, 0.2f), position.Z);
            var outcome = _sessions.Active.ResolvePlayerDeath(
                _sessionStates.Player, _sessionStates.GameRules, dropPosition);
            _player?.SuspendForModal();
            SyncHeldBlock();
            SendHotbarState();
            SendInventoryState();
            SendPlayerDeathState(outcome);
            SendPlayerModeState();
        }
    }

    private void SendPlayerDeathState(
        PlayerDeathOutcome? outcome = null)
    {
        if (_worldSeed is null) return;
        SendWebUi("game.player.death", new
        {
            keepInventory = outcome?.InventoryKept ??
                _sessionStates.GameRules.KeepInventory,
            droppedStacks = outcome?.DroppedStacks ?? 0,
            dropCapacityExceeded = outcome?.DropCapacityExceeded ?? false,
            outcomeKnown = outcome.HasValue,
        });
    }

    private void TryRespawnPlayer()
    {
        if (_worldSeed is null || !_worldReadySent ||
            _sessions.IsTransitioning || _player is null ||
            !_sessionStates.Player.Health.IsDead)
            return;

        // Request the generated safe spawn; re-enter the current Sphere
        // through the same resident-chunk and loading lifecycle as travel.
        _sessions.Active.PrepareGeneratedSpawn();
        if (BeginDimensionTransition(
            _dimension.Id, _sessions.Active.InitialPlayerPosition))
            _respawnArrival = true;
    }

    private void SendWorldHudState(
        bool force = false)
    {
        if (_clientPreferences.HideHud || _player is null)
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

    private object? InventorySlotView(InventoryStack? stack) =>
        InventorySlotPresentation(stack, includeDefaultIcon: false);

    private object? HotbarSlotView(InventoryStack? stack) =>
        InventorySlotPresentation(stack, includeDefaultIcon: true);

    private object? InventorySlotPresentation(
        InventoryStack? stack, bool includeDefaultIcon)
    {
        if (stack is null) return null;
        var customBlockState = stack.Block is { } state &&
            (state.HasMicroblockGeometry ||
             state.SurfaceState.Layers.Count > 0 ||
             state.Cell != new VoxelCell(state.Cell.Block));
        return new
        {
            id = stack.Id,
            kind = stack.Kind.ToString().ToLowerInvariant(),
            quantity = stack.Quantity,
            metadata = stack.Entry.Metadata,
            durability = EquipmentDurabilityView(stack),
            // Catalog icons are resolved client-side. Only authored state
            // variants send additional PNGs; the HUD needs its own baseline.
            blockPreview = includeDefaultIcon || customBlockState
                ? BlockPreviewForInventory(stack.Entry) : null,
            iconUrl = includeDefaultIcon
                ? InventoryIconForEntry(stack.Entry) : null,
        };
    }

    private string? InventoryIconForEntry(InventoryEntry entry)
    {
        if (entry.Kind == InventoryEntryKind.Block)
            return null;
        if (entry.Kind == InventoryEntryKind.Object &&
            _objects.TryGet(entry.Id, out var obj) &&
            obj is { Tint: not BlockTint.None })
            return _blockPreviewRenderer.ObjectDataUri(obj,
                PlayerInventoryIconTint(obj.Tint, obj.TintFallback));
        if (entry.Kind == InventoryEntryKind.Layer &&
            _layers.TryGet(entry.Id, out var layer) &&
            layer is { Tint: not BlockTint.None })
            return _blockPreviewRenderer.LayerDataUri(layer,
                PlayerInventoryIconTint(layer.Tint,
                    new BlockPreviewColor(255, 255, 255)));
        var choice = _inventoryCatalog.Choices.FirstOrDefault(
            item => item.Entry.Equals(entry));
        return choice?.IconResourcePath is { } path
            ? IconDataUri(path) : null;
    }

    private object? EquipmentDurabilityView(InventoryStack stack)
    {
        if (stack.Kind != InventoryEntryKind.Item ||
            !_items.TryGet(stack.Id, out var definition) ||
            definition?.MaxDurability is not { } maximum)
            return null;
        return new
        {
            current = EquipmentDurability.Remaining(stack.Entry, maximum),
            maximum,
        };
    }

    private void SendHotbarState()
    {
        var inventory = _sessionStates.Player.Inventory;
        SendWebUi(
            "game.hud.hotbar",
            new
            {
                selectedIndex = inventory.SelectedSlot,
                slots = Enumerable.Range(0, PlayerInventory.HotbarSlots)
                    .Select(index => HotbarSlotView(
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
            equipment = Enum.GetValues<EquipmentSlot>()
                .Select(slot => InventorySlotView(inventory.EquipmentAt(slot)))
                .ToArray(),
            recipes = _crafting.Recipes.Select(recipe =>
            {
                var availability = _crafting.Availability(inventory, recipe.Id);
                return new
                {
                    id = recipe.Id,
                    resultId = recipe.Result.Item,
                    outputQuantity = recipe.Result.Quantity,
                    ingredients = recipe.Ingredients.Select(ingredient => new
                    {
                        id = ingredient.Item,
                        required = ingredient.Quantity,
                        available = inventory.ItemQuantity(ingredient.Item),
                    }).ToArray(),
                    craftable = availability == InventoryCraftingResult.Crafted,
                    unavailableReason = availability == InventoryCraftingResult.Crafted
                        ? null : availability.ToString(),
                };
            }).ToArray(),
        });
    }

    private void SendStorageBoxState()
    {
        if (_worldSeed is null) return;
        var snapshot = _sessions.Active.ActiveStorageBox;
        _publishedStorageRevision = _sessions.Active.StorageBoxRevision;
        SendWebUi("game.storage_box.state", new
        {
            open = snapshot is not null,
            position = snapshot is null ? null : new
            {
                x = snapshot.Position.X,
                y = snapshot.Position.Y,
                z = snapshot.Position.Z,
            },
            slots = snapshot?.Slots.Select(InventorySlotView).ToArray()
                ?? Array.Empty<object?>(),
            revision = _sessions.Active.StorageBoxRevision,
        });
    }

    private void PublishStorageBoxChanges()
    {
        _sessions.Active.RefreshStorageBoxAvailability();
        if (_publishedStorageRevision == _sessions.Active.StorageBoxRevision)
            return;
        SendStorageBoxState();
        if (_sessions.Active.ActiveStorageBox is not null) return;

        // A removed/unloaded container closes without consuming the cursor.
        // The authoritative player inventory retains it until returned.
        SyncHeldBlock();
        SendHotbarState();
        SendInventoryState();
        _player?.ResumeAfterKeyCapture();
        CallDeferred(nameof(ResumeGameplayAfterInventory));
    }

    private void SendCreativeCatalog()
    {
        MarkCreativeCatalogTintPublished();
        SendWebUi("game.inventory.catalog", new
        {
            categories = _inventoryCatalog.Categories.Select(category => new
            {
                id = category.Id,
                order = category.Order,
                iconUrl = IconDataUri(category.Icon),
            }).ToArray(),
            everythingIconUrl = IconDataUri(
                "textures/creative_categories/everything.png"),
            items = _inventoryCatalog.Choices
                .Select(choice => new
                {
                    id = choice.Entry.Id,
                    kind = choice.Entry.Kind.ToString().ToLowerInvariant(),
                    name = choice.Entry.Id,
                    category = choice.Category,
                    metadata = choice.Entry.Metadata,
                    iconUrl = choice.Entry.Kind == InventoryEntryKind.Object &&
                        _objects.TryGet(choice.Entry.Id, out var objectDefinition) &&
                        objectDefinition is { Tint: not BlockTint.None }
                        ? _blockPreviewRenderer.ObjectDataUri(objectDefinition,
                            PlayerInventoryIconTint(
                                objectDefinition.Tint, objectDefinition.TintFallback))
                        : choice.Entry.Kind == InventoryEntryKind.Layer &&
                          _layers.TryGet(choice.Entry.Id, out var layerDefinition) &&
                          layerDefinition is { Tint: not BlockTint.None }
                            ? _blockPreviewRenderer.LayerDataUri(layerDefinition,
                                PlayerInventoryIconTint(layerDefinition.Tint,
                                    new BlockPreviewColor(255, 255, 255)))
                        : choice.IconResourcePath is { } path
                            ? IconDataUri(path) : null,
                    blockPreview = BlockPreviewForInventory(choice.Entry),
                })
                .ToArray(),
        });
    }

    private object? BlockPreviewForInventory(InventoryEntry entry)
    {
        if (entry.Kind != InventoryEntryKind.Block)
            return null;

        var blockId = _blocks.GetId(entry.Id);
        var block = _blocks.GetDefinition(blockId);
        var cell = entry.Block?.Cell ?? new VoxelCell(blockId);
        return BlockPreviewForCell(block, cell,
            entry.Block?.SurfaceState, mask: entry.Block?.MicroblockMask,
            inventoryTint: PlayerInventoryIconTint(
                block.Tint, block.PreviewColor));
    }

    private object? BlockPreviewForCell(
        BlockDefinition block, VoxelCell cell,
        BlockSurfaceState? surface = null, WorldVoxelCoord? target = null,
        BlockFace? targetFace = null, MicroblockMask? mask = null,
        BlockPreviewColor? inventoryTint = null)
    {
        // World targets sample real biome tint for both host block and
        // attached faces; catalog/inventory icons keep authored fallback.
        BiomeTintSampleGrid? tintGrid = null;
        if (target is { } position &&
            (block.Tint != BlockTint.None ||
             surface is { Layers.Count: > 0 }))
        {
            var coord = VoxelCoordinates.FromWorld(
                position.X, position.Y, position.Z).Chunk;
            tintGrid = _sessions.Active.Generator.Tints.SampleChunkGrid(
                coord.X, coord.Z);
        }
        BlockPreviewColor Tint(BlockTint tint)
        {
            if (target is not { } position || tintGrid is null)
                return new BlockPreviewColor(255, 255, 255);
            // The host has an authored fallback for its own tint channel;
            // attached layers without a palette continue to default white.
            var fallback = tint == block.Tint
                ? block.PreviewColor
                : new BlockPreviewColor(255, 255, 255);
            return BlockPreviewColor.FromNormalized(
                tintGrid.Resolve(tint, fallback, position.X, position.Z));
        }
        var baseTint = inventoryTint ?? (
            block.Tint != BlockTint.None && target is not null
                ? Tint(block.Tint)
                : (BlockPreviewColor?)null);
        // SourceFaceForWorldFace is the same Core owner used by terrain
        // meshing and targeting. A laid-down log, for example, must show
        // its authored end grain on the face which actually points upward.
        string? Texture(BlockFace face)
        {
            var sourceFace = BlockFaceTransform.SourceFaceForWorldFace(
                face, cell, block);
            var uvRotation = BlockUvRotation.ForWorldFace(
                face, cell.Orientation, cell.TextureRotation,
                block.RotateTexture.Rotates(sourceFace));
            if (surface is { Layers.Count: > 0 })
            {
                var attachments = BlockPreviewLayerPlan.ForWorldFace(
                    surface, _layers, face);
                if (attachments.Count > 0)
                    return _blockPreviewRenderer.LayeredFaceDataUri(
                        block, sourceFace, uvRotation, attachments, Tint,
                        baseTint);
            }
            return _blockPreviewRenderer.FaceDataUri(
                block, sourceFace, uvRotation, baseTint);
        }

        // The inventory and target HUD share canonical 3D mesh icons for
        // all geometry blocks, including ordinary cubes. The previous CSS
        // three-face target fallback stretched textures without perspective.
        // A targeted attached layer on a hidden face still needs its own 2D
        // face preview because that surface is occluded from this camera.
        var obscuredLayer = targetFace is { } touchedFace &&
            surface is not null && surface.TryGetTop(touchedFace, out _) &&
            touchedFace is not (BlockFace.Top or BlockFace.Front or BlockFace.Right);
        if (!obscuredLayer && block.Visual.Texture is null)
        {
            var occupied = cell.HasMicroblockGeometry
                ? mask ?? (target is { } previewPosition
                    ? _world.GetMicroblockMaskOrEmpty(previewPosition)
                    : MicroblockMask.Empty)
                : MicroblockMask.Empty;
            if (!cell.HasMicroblockGeometry || !occupied.IsEmpty)
            {
                var snapshot = new BlockStateSnapshot(cell, occupied, surface);
                var geometry = _voxelIcons.Render(block, snapshot, baseTint);
                if (geometry is not null)
                    return new
                    {
                        kind = "sprite",
                        front = geometry,
                        top = (string?)null,
                        right = (string?)null,
                    };
            }
        }

        // If the interacted layer lies on a back/left/bottom face, show
        // its actual 2D face rather than a cube that conceals that layer.
        if (targetFace is { } touched && surface is not null &&
            surface.TryGetTop(touched, out _) &&
            touched is not (BlockFace.Top or BlockFace.Front or BlockFace.Right))
        {
            var faceTexture = Texture(touched);
            if (faceTexture is not null)
                return new
                {
                    kind = "sprite",
                    front = faceTexture,
                    top = (string?)null,
                    right = (string?)null,
                };
        }

        var front = Texture(BlockFace.Front);
        if (front is null) return null;

        var isCube = block.Visual.Texture is null &&
            block.Shape.Kind == BlockShapeKind.Cube &&
            !cell.HasMicroblockGeometry;
        return new
        {
            kind = isCube ? "cube" : "sprite",
            front,
            top = isCube ? Texture(BlockFace.Top) : null,
            right = isCube ? Texture(BlockFace.Right) : null,
        };
    }

    /// <summary>
    /// Objects and targeted attached Layers use the target block's actual
    /// spatial biome tint, not the player's hotbar/Creative palette.
    /// </summary>
    private BlockPreviewColor ResolveHudTint(
        BlockTint tint, BlockPreviewColor fallback, WorldVoxelCoord position)
    {
        var coord = VoxelCoordinates.FromWorld(
            position.X, position.Y, position.Z).Chunk;
        var sample = _sessions.Active.Generator.Tints
            .SampleChunkGrid(coord.X, coord.Z)
            .Resolve(tint, fallback, position.X, position.Z);
        return BlockPreviewColor.FromNormalized(sample);
    }

    private BlockPreviewColor ResolveObjectHudTint(
        ObjectDefinition definition, WorldVoxelCoord support) =>
        ResolveHudTint(definition.Tint, definition.TintFallback, support);

    private string? ResolveTargetLayerIcon(
        string? layerId, WorldVoxelCoord position)
    {
        if (layerId is null ||
            !_layers.TryGet(layerId, out var layer) || layer is null)
            return null;
        if (layer.Tint == BlockTint.None)
            return IconDataUri(layer.Texture);

        return _blockPreviewRenderer.LayerDataUri(layer,
            ResolveHudTint(layer.Tint,
                new BlockPreviewColor(255, 255, 255), position));
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

    private void PickTargetBlock()
    {
        if (!_worldReadySent || _sessions.IsTransitioning || _inventoryOpen ||
            _brushPaletteOpen || _chat.IsOpen ||
            _player is not { IsMouseCaptured: true } ||
            _sessionStates.Player.GameMode != PlayerGameMode.Creative ||
            !_sessionStates.Player.CanInteract)
            return;

        if (CurrentObjectTarget() is { } objectHit)
        {
            if (_inventoryCatalog.TryResolve(InventoryEntryKind.Object,
                    objectHit.Object.Id, null, null, out var objectEntry) &&
                objectEntry is not null &&
                _sessionStates.Player.Inventory.ReplaceSelectedCreative(objectEntry))
            {
                SyncHeldBlock();
                SendHotbarState();
                SendInventoryState();
                SendTargetHudState(force: true);
            }
            return;
        }

        if (!TryGetTarget(out var hit)) return;
        var target = BlockInteractionResolver.ResolveBreak(_world, hit);
        if (!target.Accepted) return;
        var definition = _blocks.GetDefinition(target.Cell.Block);
        // Pickup sprites are represented by real collectible items in Asteria.
        var kind = definition.Interaction == BlockInteractionKind.Pickup
            ? InventoryEntryKind.Item : InventoryEntryKind.Block;
        var id = definition.Interaction == BlockInteractionKind.Pickup
            ? definition.PickupItemId! : definition.Id;
        if (!_inventoryCatalog.TryResolve(kind, id, null, null, out var entry) ||
            entry is null ||
            !_sessionStates.Player.Inventory.ReplaceSelectedCreative(entry))
            return;

        SyncHeldBlock();
        SendHotbarState();
        SendInventoryState();
        SendTargetHudState(force: true);
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
        if (_inventoryOpen) _playerPortrait?.RequestCapture();
        SendTargetHudState(force: true);
    }

    private Color? ResolveSelectedToolTint()
    {
        var id = _sessions.Active.Tools.SelectedBrushDyeId;
        if (id is null || !_dyes.TryGet(id, out var dye) || dye is null)
            return null;
        var rgb = dye.Rgb;
        return new Color(rgb.X, rgb.Y, rgb.Z, 1f);
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
        _player?.ResumeAfterKeyCapture();
        CallDeferred(nameof(ResumeGameplayAfterInventory));
    }

    private void OpenChat()
    {
        if (!_worldReadySent || _inventoryOpen || _brushPaletteOpen ||
            _keybindCapture.IsCapturing || _sessions.IsTransitioning ||
            _player is null || !_player.IsMouseCaptured || !_chat.Open())
            return;

        // Publish the open chat before releasing capture: otherwise WebUI
        // mistakes the capture-loss event for a request to open pause.
        SendChatState();
        _player.SuspendForModal();
        // Native WebView focus happens after gameplay releases keyboard focus.
        // Refocus the mounted chat field only after that handoff completes.
        CallDeferred(nameof(FocusChatInput));
    }

    private void FocusChatInput()
    {
        if (_chat.IsOpen && _player is { IsMouseCaptured: false })
            _webUi.Call("focus_chat_input");
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
            ResumeGameplayOrPauseAfterFocusLoss();
    }

    /// <summary>
    /// Closing a modal during Alt+Tab must not leave a live world with
    /// neither mouse capture nor a visible pause menu. The native player
    /// owns the focus guard; the UI gets an explicit pause only when that
    /// guard declined to capture due to OS focus loss.
    /// </summary>
    private void ResumeGameplayOrPauseAfterFocusLoss()
    {
        if (_player is null) return;
        _player.ResumeGameplay();
        if (!_player.IsMouseCaptured && !GetWindow().HasFocus())
            SendWebUi("game.ui.pause", new { });
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

    private void ExecuteChatMessageAction(JsonElement message)
    {
        if (!_worldReadySent || _sessions.IsTransitioning ||
            !_sessionStates.Player.CanInteract || !_chat.IsOpen && _chatFeedbackSeconds <= 0.0 ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("id", out var idElement) ||
            idElement.ValueKind != JsonValueKind.String ||
            !ulong.TryParse(idElement.GetString(),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var id) ||
            id == 0)
            return;

        if (_chat.TryGetWarpTarget(id, out var target) && target is not null)
        {
            ExecuteChatCommand(new ParsedChatCommand(
                ChatCommandKind.Warp, Option: target.Dimension,
                X: target.X, Y: target.Y, Z: target.Z));
        }
        else if (_chat.TryGetStructureFileName(id, out var fileName) &&
                 fileName is not null)
        {
            OpenChatStructureFile(fileName);
        }
        else return;

        _chatFeedbackSeconds = 10.0;
        SendChatState();
    }

    private void OpenChatStructureFile(string fileName)
    {
        // The target is an opaque, Core-authorized export filename, not a
        // URI or path supplied by WebUI.
        var directory = ProjectSettings.GlobalizePath("user://exports/structures");
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
        {
            ChatFeedback("chat.command.failed", error: true);
            return;
        }

        try
        {
            var directoryUri = new UriBuilder
            {
                Scheme = Uri.UriSchemeFile,
                Path = Path.GetFullPath(directory) + Path.DirectorySeparatorChar,
            }.Uri.AbsoluteUri;
            if (OS.ShellOpen(directoryUri) != Error.Ok)
                ChatFeedback("chat.command.failed", error: true);
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or UriFormatException)
        {
            GD.PushWarning($"chat.structure_file.open: {exception.Message}");
            ChatFeedback("chat.command.failed", error: true);
        }
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
            case ChatCommandKind.Place:
                ExecuteChatPlace(command);
                break;
            case ChatCommandKind.LocateBiome:
            case ChatCommandKind.LocateStructure:
                ExecuteChatLocate(command);
                break;
            case ChatCommandKind.Kill:
                ExecuteChatKill();
                break;
            case ChatCommandKind.Modify:
                ExecuteChatModify(command);
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
            if (!BeginDimensionTransition(
                    target.Id, position, purpose: DimensionTravelPurpose.ChatWarp))
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
        var preferred = new NVector3(
            location.X + 0.5f, location.Y, location.Z + 0.5f);
        var feet = new WorldVoxelCoord(location.X, location.Y, location.Z);
        var head = new WorldVoxelCoord(location.X, location.Y + 1, location.Z);
        if (!_world.IsLoadedAt(feet) || !_world.IsLoadedAt(head))
        {
            // A distant warp uses the same cooperative retirement, archive,
            // restore and progress pipeline as an inter-Sphere transition.
            if (!BeginDimensionTransition(
                    target.Id, preferred, purpose: DimensionTravelPurpose.ChatWarp))
                ChatFeedback("chat.command.warp.failed", error: true);
            else
                ChatFeedback("chat.command.warp.start", error: false,
                    ("position", $"({location.X}, {location.Z}, {location.Y})"));
            return;
        }

        var resident = ResidentWarpDestinationQuery.Find(_world, preferred);
        if (resident is not { } destination)
        {
            ChatFeedback("chat.command.warp.failed", error: true);
            return;
        }

        _player.ClearGameplayInput();
        _player.GlobalPosition = new Vector3(
            destination.X, destination.Y, destination.Z);
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

    private void ExecuteChatModify(ParsedChatCommand command)
    {
        var tag = command.Argument!;
        if (!CreatureMetaTags.TryNormalize(tag, out var canonical))
        {
            ChatFeedback("chat.command.meta.unknown", error: true,
                ("tag", tag));
            return;
        }

        if (_player is null)
        {
            ChatFeedback("chat.command.target.none", error: true);
            return;
        }
        var (from, to) = _player.GetInteractionRay(InteractionDistance);
        var target = _sessions.Active.FindCreatureTarget(
            new NVector3(from.X, from.Y, from.Z),
            new NVector3(to.X - from.X, to.Y - from.Y, to.Z - from.Z),
            InteractionDistance);
        if (target is not { } hit)
        {
            ChatFeedback("chat.command.target.none", error: true);
            return;
        }

        var action = command.Option switch
        {
            "add" => CreatureMetaTagAction.Add,
            "remove" => CreatureMetaTagAction.Remove,
            "edit" => CreatureMetaTagAction.Edit,
            _ => throw new InvalidOperationException(
                "Parser accepted an unsupported metadata action."),
        };
        if (!_sessions.Active.TryChangeCreatureMetaTag(
                hit.Creature.Id, action, canonical, command.Value, out var error))
        {
            var key = error switch
            {
                CreatureMetaTagError.UnknownTag => "chat.command.meta.unknown",
                CreatureMetaTagError.AlreadyPresent => "chat.command.meta.exists",
                CreatureMetaTagError.NotSet => "chat.command.meta.notSet",
                CreatureMetaTagError.InvalidValue => "chat.command.failed",
                _ => "chat.command.target.unavailable",
            };
            ChatFeedback(key, error: true, ("tag", canonical));
            return;
        }

        var position = hit.Creature.Position;
        var feedback = action switch
        {
            CreatureMetaTagAction.Add => "chat.command.modify.addSuccess",
            CreatureMetaTagAction.Remove => "chat.command.modify.removeSuccess",
            _ => "chat.command.modify.editSuccess",
        };
        ChatFeedback(feedback, error: false,
            ("tag", canonical),
            ("name", hit.Creature.DefinitionId),
            ("position", $"({Mathf.FloorToInt(position.X)}, " +
                $"{Mathf.FloorToInt(position.Z)}, {Mathf.FloorToInt(position.Y)})"));
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

        var tags = default(CreatureMetaTags);
        if (command.Option is { } tag &&
            !tags.TryChange(CreatureMetaTagAction.Add,
                tag, null, out tags, out _))
        {
            ChatFeedback("chat.command.meta.unknown", error: true,
                ("tag", tag));
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
        if (!_sessions.Active.TrySpawnCreature(id, feet, tags))
        {
            ChatFeedback("chat.command.spawn.failed", error: true, ("id", id));
            return;
        }

        ChatFeedback("chat.command.spawn.success", error: false,
            ("name", id),
            ("position", $"({safe.Value.X}, {safe.Value.Z}, {safe.Value.Y})"));
    }

    private void ExecuteChatPlace(ParsedChatCommand command)
    {
        var id = command.Argument!;
        var isSet = _structureSets.TryGet(id, out _);
        if (!isSet && !_structures.ResolvesReference(id))
        {
            ChatFeedback("chat.command.place.unknownStructure",
                error: true, ("id", id));
            return;
        }
        var variation = command.Option is { } number
            ? int.Parse(number, System.Globalization.CultureInfo.InvariantCulture)
            : 1;
        if (isSet && command.Option is not null)
        {
            ChatFeedback("chat.command.place.unknownVariation",
                error: true, ("id", id), ("variation", variation.ToString()),
                ("count", "0"));
            return;
        }
        if (!isSet && variation > _structures.ResolveReference(id).Count)
        {
            ChatFeedback("chat.command.place.unknownVariation",
                error: true, ("id", id), ("variation", variation.ToString()),
                ("count", _structures.ResolveReference(id).Count.ToString()));
            return;
        }
        if (_player is null)
        {
            ChatFeedback("chat.command.place.playerUnavailable", error: true);
            return;
        }

        var (_, target) = _player.GetInteractionRay(InteractionDistance);
        var result = _sessions.Active.TryPlaceManualStructure(
            id, command.Option is null ? null : variation,
            Mathf.FloorToInt(target.X), Mathf.FloorToInt(target.Z),
            _player.CollisionBounds);
        if (result != ManualStructurePlacementResult.Placed)
        {
            var message = result switch
            {
                ManualStructurePlacementResult.UnknownReference =>
                    "chat.command.place.unknownStructure",
                ManualStructurePlacementResult.NonResident =>
                    "chat.command.place.noLoadedSpace",
                ManualStructurePlacementResult.ProtectedVoxel or
                    ManualStructurePlacementResult.PlayerCollision =>
                    "chat.command.place.noSafeSpace",
                _ => "chat.command.place.noGround",
            };
            ChatFeedback(message, error: true, ("id", id));
            return;
        }

        ChatFeedback("chat.command.place.success", error: false,
            ("name", isSet ? id : _structures.ResolveReference(id)[variation - 1].Id),
            ("id", id));
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
        if (command.Kind == ChatCommandKind.LocateStructure)
        {
            if (_structureSets.TryGet(id, out var set))
            {
                if (command.Option is not null)
                {
                    ChatFeedback("chat.command.locate.setNoVariations", error: true,
                        ("id", id));
                    return;
                }
                if (!set.Locatable)
                {
                    ChatFeedback("chat.command.locate.setNotLocatable", error: true,
                        ("id", id));
                    return;
                }
                if (!_sessions.Active.Generator.HasGeneratedSurfaceStructure(id))
                {
                    ChatFeedback("chat.command.locate.setNotGenerated", error: true,
                        ("id", id));
                    return;
                }
            }
            else if (_structures.ResolvesReference(id))
            {
                var members = _structures.ResolveReference(id);
                var variation = 1;
                if (command.Option is { } selection &&
                    (!PlayerChatCommandProcessor.TryVariation(selection, out variation) ||
                     variation > members.Count))
                {
                    ChatFeedback("chat.command.locate.unknownVariation", error: true,
                        ("id", id), ("variation", selection),
                        ("count", members.Count.ToString()));
                    return;
                }
                var member = members[variation - 1];
                if (!member.Locatable)
                {
                    ChatFeedback("chat.command.locate.structureNotLocatable",
                        error: true, ("id", id));
                    return;
                }
                if (!_sessions.Active.Generator.HasGeneratedSurfaceStructure(id))
                {
                    ChatFeedback("chat.command.locate.structureNotGenerated",
                        error: true, ("id", id));
                    return;
                }
                if (command.Option is not null)
                    structureId = member.Id;
            }
            else
            {
                ChatFeedback("chat.command.locate.unknownStructure", error: true,
                    ("id", id));
                return;
            }
        }
        else
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
            _chat.AppendLocated(
                "chat.command.locate.found",
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["name"] = result.Id,
                    ["position"] = $"({result.X}, {result.Z}, {result.Y})",
                },
                new PlayerChatWarpTarget(
                    result.X, result.Y, result.Z, _dimension.Id.Value));
        }
        else
        {
            ChatFeedback("chat.command.locate.notFound", error: true,
                ("name", searchedId),
                ("radius", ChatLocateController.MaxSearchRadius.ToString()));
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
        var generator = _worldSeed is null ? null : _sessions.Active.Generator;
        var structureCandidates = ChatStructureCompletionCatalog.Create(
            _structures, _structureSets,
            generator is null ? null : generator.HasGeneratedSurfaceStructure);

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
                action = line.WarpTarget is not null ? "warp" :
                    line.StructureFileName is not null ? "structure_file" : null,
            }).ToArray(),
            commands = PlayerChatCommandProcessor.SupportedCommands,
            catalog = new
            {
                creatures = _creatures.Definitions.Select(definition => definition.Id)
                    .Take(512).ToArray(),
                biomes = _worldSeed is null
                    ? Array.Empty<string>()
                    : _dimension.SurfaceBiomes.OrderBy(id => id, StringComparer.Ordinal)
                        .Take(512).ToArray(),
                structures = structureCandidates.Structures,
                locatableStructures = structureCandidates.LocatableStructures,
                variations = _structures.Definitions()
                    .Where(definition => definition.GroupReference is not null)
                    .GroupBy(definition => definition.GroupReference!, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .Take(256)
                    .Select(group => new
                    {
                        id = group.Key,
                        ids = group.Select(definition => definition.Id)
                            .OrderBy(id => id, StringComparer.Ordinal)
                            .Take(128).ToArray(),
                    }).ToArray(),
                dimensions = _dimensions.Definitions()
                    .Select(definition => definition.Id.Value)
                    .OrderBy(id => id, StringComparer.Ordinal).Take(64).ToArray(),
                position = _player is { } player
                    ? new
                    {
                        x = Mathf.FloorToInt(player.GlobalPosition.X),
                        y = Mathf.FloorToInt(player.GlobalPosition.Y),
                        z = Mathf.FloorToInt(player.GlobalPosition.Z),
                    }
                    : null,
            },
        });
    }

    private void PollPlayerPortrait()
    {
        if (_playerPortrait?.TryTake(out var data) == true)
            SendWebUi("game.player.portrait", new { imageUrl = data });
    }

    private void RotatePlayerPortrait(JsonElement message)
    {
        if (!_inventoryOpen || _playerPortrait is null ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("deltaX", out var dx) ||
            !dx.TryGetSingle(out var delta) || !float.IsFinite(delta))
            return;
        _playerPortrait.Rotate(delta);
    }

    private void OpenInventory()
    {
        if (!_worldReadySent || _inventoryOpen || _chat.IsOpen ||
            _sessions.IsTransitioning ||
            !_sessionStates.Player.CanInteract || _player is null)
            return;
        _inventoryOpen = true;
        _player.SuspendForModal();
        SyncInventoryIconTint();
        if (CreativeCatalogTintChanged())
            SendCreativeCatalog();
        SendInventoryState();
        _playerPortrait?.RequestCapture();
    }

    private void CloseInventory()
    {
        if (!_inventoryOpen || !_sessionStates.Player.CanInteract) return;
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
        if (!_inventoryOpen && _sessions.Active.ActiveStorageBox is null &&
            !_brushPaletteOpen && !_keybindCapture.IsCapturing &&
            _sessionStates.Player.CanInteract)
            ResumeGameplayOrPauseAfterFocusLoss();
    }

    private void CloseStorageBox()
    {
        if (_sessions.Active.ActiveStorageBox is null ||
            !_sessionStates.Player.CanInteract) return;
        if (!_sessionStates.Player.Inventory.TryReturnCursor())
        {
            SendWebUi("game.storage_box.error",
                new { code = "InventoryFull" });
            return;
        }

        _sessions.Active.CloseStorageBox();
        SyncHeldBlock();
        SendStorageBoxState();
        SendInventoryState();
        SendHotbarState();
        _player?.ResumeAfterKeyCapture();
        CallDeferred(nameof(ResumeGameplayAfterInventory));
    }

    private void HandleStorageBoxSlot(JsonElement message)
    {
        if (_sessions.Active.ActiveStorageBox is null ||
            !_sessionStates.Player.CanInteract ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("index", out var value) ||
            !value.TryGetInt32(out var index)) return;

        var action = ReadInventorySlotAction(payload);
        if (action is null) return;
        var inventory = _sessionStates.Player.Inventory;
        var changed = action switch
        {
            "right" => _sessions.Active.TryClickStorageBox(
                index, inventory, rightButton: true),
            "quick" => _sessions.Active.TryQuickTransferStorageBox(index, inventory),
            _ => _sessions.Active.TryClickStorageBox(index, inventory),
        };
        if (!changed) return;
        SyncHeldBlock();
        SendHotbarState();
        SendInventoryState();
        SendStorageBoxState();
    }

    private void HandleEquipmentSlot(JsonElement message)
    {
        if (!_inventoryOpen || !_worldReadySent ||
            _sessions.IsTransitioning || !_sessionStates.Player.CanInteract ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("index", out var index) ||
            !index.TryGetInt32(out var number) ||
            number is < 0 or >= PlayerInventory.EquipmentSlots)
            return;

        var inventory = _sessionStates.Player.Inventory;
        if (!inventory.ClickEquipment(
                (EquipmentSlot)number, _inventoryCatalog.CanEquip))
            return;
        SendInventoryState();
        _playerPortrait?.RequestCapture();
    }

    private void HandleInventorySlot(JsonElement message)
    {
        if ((!_inventoryOpen && _sessions.Active.ActiveStorageBox is null) ||
            !_sessionStates.Player.CanInteract ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("index", out var indexValue) ||
            !indexValue.TryGetInt32(out var index)) return;
        var action = ReadInventorySlotAction(payload);
        if (action is null) return;
        var inventory = _sessionStates.Player.Inventory;
        var inStorage = _sessions.Active.ActiveStorageBox is not null;
        var changed = action switch
        {
            "right" => inventory.ClickSlot(index, rightButton: true),
            "quick" when inStorage =>
                _sessions.Active.TryQuickTransferPlayerToStorage(index, inventory),
            "quick" => inventory.QuickMoveSlot(index),
            _ => inventory.ClickSlot(index),
        };
        if (!changed) return;
        SyncHeldBlock();
        SendHotbarState();
        SendInventoryState();
        if (_inventoryOpen) _playerPortrait?.RequestCapture();
        if (inStorage) SendStorageBoxState();
    }

    // The browser communicates semantic slot intent, never raw gameplay
    // pointer input. Reject unsupported actions before mutating any state.
    private static string? ReadInventorySlotAction(JsonElement payload)
    {
        if (!payload.TryGetProperty("action", out var value))
            return "left";
        if (value.ValueKind != JsonValueKind.String)
            return null;
        var action = value.GetString();
        return action is "left" or "right" or "quick" ? action : null;
    }

    private void HandleInventoryCraft(JsonElement message)
    {
        if (!_inventoryOpen || !_worldReadySent || _sessions.IsTransitioning ||
            !_sessionStates.Player.CanInteract ||
            !message.TryGetProperty("payload", out var payload) ||
            payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("recipeId", out var recipeValue) ||
            recipeValue.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(recipeValue.GetString()))
            return;

        var recipeId = recipeValue.GetString()!;
        var inventory = _sessionStates.Player.Inventory;
        var result = _crafting.Craft(inventory, recipeId);
        if (result == InventoryCraftingResult.Crafted)
        {
            SyncHeldBlock();
            SendHotbarState();
            SendInventoryState();
        }

        SendWebUi("game.inventory.crafting_result", new
        {
            code = result.ToString(),
            recipeId,
        });
    }

    private void HandleCreativePick(JsonElement message)
    {
        if (!_inventoryOpen || !_sessionStates.Player.CanInteract ||
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

        var fillStack = payload.TryGetProperty("fillStack", out var fill);
        if (fillStack && fill.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return;
        if (!_inventoryCatalog.TryResolve(
                kind, id, metadataKey, metadataValue, out var entry) ||
            entry is null ||
            !_sessionStates.Player.Inventory.TryCreativePick(
                entry, fillStack: fillStack && fill.GetBoolean())) return;
        SendInventoryState();
    }

    private (NVector3 Position, NVector3 Velocity) PlayerDropLaunch()
    {
        var ray = _player!.GetInteractionRay(2f);
        var direction = (ray.To - ray.From).Normalized();
        var from = ray.From + direction * 0.6f;
        return (
            new NVector3(from.X, MathF.Max(from.Y, 0.2f), from.Z),
            new NVector3(direction.X * 3f,
                direction.Y * 3f + 2f, direction.Z * 3f));
    }

    private void DropInventoryCursor()
    {
        if (!_inventoryOpen || _player is null ||
            !_sessionStates.Player.CanInteract ||
            _sessionStates.Player.Inventory.Cursor is null) return;

        var (position, velocity) = PlayerDropLaunch();
        if (!_sessions.Active.TryDropInventoryCursor(
                _sessionStates.Player.Inventory, position, velocity))
        {
            SendWebUi("game.inventory.error",
                new { code = "DropCapacityExceeded" });
            return;
        }
        SendInventoryState();
    }

    private void DropSelectedItem()
    {
        if (_player is null || _inventoryOpen ||
            !_sessionStates.Player.CanInteract) return;
        var inventory = _sessionStates.Player.Inventory;
        var selected = inventory.SelectedStack;
        if (selected is null) return;
        // Core reserves drop capacity before consuming one selected unit.
        // Saturation never evicts an older physical item from the world.
        var (position, velocity) = PlayerDropLaunch();
        if (!_blockEntities.TryDropSelectedItem(inventory, position, velocity))
            return;
        SyncHeldBlock();
        SendHotbarState();
        SendInventoryState();
    }

    private void AdvanceItemPickup(double delta)
    {
        if (_player is null || _blockEntities.DroppedCount == 0 ||
            !_sessionStates.Player.CanInteract)
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
            _sessionStates.Player.Inventory.InsertUpTo);
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
        if (_clientPreferences.HideHud) return;
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
        if (_clientPreferences.HideHud ||
            _sessionStates.Player.GameMode.IsSpectator())
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
            var portraitUrl = _creaturePortrait?.GetOrRequest(creature.DefinitionId);
            var creatureSnapshot = (creature.Id.Value, creature.Health,
                (float)maximum, portraitUrl is not null);
            if (force || _lastCreatureHud != creatureSnapshot)
            {
                SendWebUi("game.hud.target_entity", new
                {
                    name = creature.DefinitionId,
                    portraitUrl,
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
            // The creature HUD hides the block/Object panel. Clearing the
            // Object identity here ensures that returning to the same Object
            // republishes it even if its visible light and state are unchanged.
            _lastObjectHud = null;
            _targetHud.Reset();
            return;
        }

        if (_lastCreatureHud is not null)
        {
            _lastCreatureHud = null;
            SendWebUi("game.hud.target_entity", new { });
        }

        if (CurrentObjectTarget() is { } objectHit)
        {
            var objectSnapshot = TargetObjectHudSnapshot.FromWorld(_world, objectHit);
            if (force || _lastObjectHud != objectSnapshot)
            {
                _lastObjectHud = objectSnapshot;
                var definition = _objects.Get(objectHit.Object.Id);
                SendWebUi("game.hud.target", new
                {
                    kind = "object",
                    id = definition.Id,
                    name = definition.Id,
                    iconUrl = definition.Tint != BlockTint.None
                        ? _blockPreviewRenderer.ObjectDataUri(
                            definition, ResolveObjectHudTint(definition,
                                objectHit.Support))
                        : IconDataUri(definition.Icon),
                    face = objectHit.Object.Face.ToString().ToLowerInvariant(),
                    skyLight = objectSnapshot.SkyLight,
                    blockLight = objectSnapshot.BlockLight,
                    position = new
                    {
                        x = objectHit.Support.X,
                        y = objectHit.Support.Y,
                        z = objectHit.Support.Z,
                    },
                });
                SendWebUi("game.hud.prompt", new
                {
                    key = definition.Interaction == ObjectInteraction.Pickup ? "RMB" : "LMB",
                    text = definition.Interaction == ObjectInteraction.Pickup
                        ? "hud.hint.pickupObject" : "hud.hint.breakObject",
                });
            }
            _targetHud.Reset();
            return;
        }
        if (_lastObjectHud is not null)
        {
            _lastObjectHud = null;
            _targetHud.Reset();
        }

        var hit =
            _player is
                { IsMouseCaptured: true }
                ? CurrentTarget(includeFluidSource: true)
                : null;

        var fluidChanged = _targetHud.TryCaptureFluid(
            _world, hit, force, out var fluidSnapshot);
        if (fluidSnapshot is { } selectedFluid)
        {
            if (fluidChanged)
            {
                var fluid = _fluids.GetDefinition(selectedFluid.Fluid.Fluid);
                var voxel = selectedFluid.Voxel;
                SendWebUi("game.hud.target", new
                {
                    kind = "fluid",
                    id = fluid.Id,
                    name = fluid.Id,
                    fluidLevel = new
                    {
                        value = selectedFluid.Fluid.Level,
                        maximum = FluidCell.MaxLevel,
                    },
                    skyLight = selectedFluid.Light.Sky,
                    blockLight = selectedFluid.Light.BlockPeak,
                    position = new { x = voxel.X, y = voxel.Y, z = voxel.Z },
                });
                SendWebUi("game.hud.prompt", new { });
            }
            return;
        }

        if (!_targetHud.TryCapture(
                _world,
                _blocks,
                _layers,
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
                blockPreview = BlockPreviewForCell(
                    _blocks.GetDefinition(value.Cell.Block), value.Cell,
                    value.Surface, new WorldVoxelCoord(value.X, value.Y, value.Z),
                    value.HitFace),
                activeLayerId = value.ActiveLayerId,
                activeLayerIconUrl = ResolveTargetLayerIcon(
                    value.ActiveLayerId, new WorldVoxelCoord(value.X, value.Y, value.Z)),
                layers = value.Surface.Layers
                    .Select(layer => new
                    {
                        id = layer.LayerId,
                        face = layer.Face.ToString().ToLowerInvariant(),
                    })
                    .ToArray(),
                skyLight = value.SkyLight,
                blockLight = value.BlockLight,
                position = new { x = value.X, y = value.Y, z = value.Z },
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
        _lastObjectHud = null;
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

        var respawned = _respawnArrival;
        if (_respawnArrival)
        {
            if (!_sessionStates.Player.Health.Respawn())
                throw new InvalidOperationException("Respawn arrival had no dead player.");
            _sessionStates.Player.Hazards.Reset();
            _respawnArrival = false;
        }

        // A new physical environment starts a fresh exposure window while
        // authoritative HP remains shared between Spheres.
        _sessionStates.Player.Hazards.Reset();
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
        _player.Landed += OnPlayerLanded;
        _player.FluidExposureSampled += OnPlayerFluidExposure;
        _player.PlaceRequested += PlaceTargetBlock;
        _player.PickBlockRequested += PickTargetBlock;
        _player.ToolActionRequested += RotateHeldBlock;
        _player.DropItemRequested += DropSelectedItem;
        _player.HotbarSlotRequested += SelectHotbar;
        _player.MouseCaptureChanged +=
            SendMouseCaptureState;
        _player.FlightStateChanged +=
            SendPlayerModeState;
        _player.FluidContactChanged +=
            OnPlayerFluidContactChanged;
        AddChild(_player);
        // Both gameplay perspectives and inventory portrait use one bounded
        // per-player Object mesh catalog; no per-item geometry duplication.
        var heldObjectVisuals = new WorldObjectVisualCatalog(_packSelection);
        _playerModel = new PlayerModelPresentation(
            _player, _packSelection, _playerVisual,
            _sessionStates.Player.Inventory, _heldVisuals, _items,
            ResolveSelectedToolTint, heldObjectVisuals);
        _player.AddChild(_playerModel);
        _playerPortrait = new PlayerPortraitPresentation(
            _packSelection, _playerVisual, _sessionStates.Player.Inventory,
            _items, _heldVisuals, ResolveSelectedToolTint,
            heldObjectVisuals);
        AddChild(_playerPortrait);
        _playerPortrait.RequestCapture();
        if (_sessionStates.Player.Health.IsDead)
        {
            _playerModel.TryPlayAction(PlayerVisualAction.Death);
            _player.SuspendForModal();
        }

        _underwaterView =
            new UnderwaterViewPresentation(
                _player.Camera,
                _fluids);

        SendWebUi(
            "game.player_ready",
            new { controller = "fps" });
        if (respawned)
            SendWebUi("game.player.respawned", new { });
        else if (_sessionStates.Player.Health.IsDead)
            SendPlayerDeathState();
        SendPlayerModeState();
        SendPlayerVitals(force: true);
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
        if (!selection.Changed)
            return;

        _worldDiagnostics?.Write(
            $"streaming.selection center={selection.Center} " +
            $"desired={selection.DesiredCount} retained={selection.RetainedCount} " +
            $"pending={selection.PendingCount} movement={selection.MovementDirection}");
        if (!_debugHudVisible)
            return;

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
                _playerModel?.TryPlayAction(PlayerVisualAction.Hit);
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

        if (CurrentObjectTarget() is { } objectHit)
        {
            _sessions.Active.Mining.Cancel();
            PublishMiningProgress();
            var removed = _sessions.Active.ObjectInteractions.TryBreak(
                objectHit, _sessionStates.Player.GameMode);
            if (removed)
            {
                _playerModel?.TryPlayAction(PlayerVisualAction.Break);
                SendTargetHudState(force: true);
            }
            return;
        }

        if (!TryGetTarget(out var hit))
            return;

        if (_sessionStates.Player.GameMode != PlayerGameMode.Survival)
            _playerModel?.TryPlayAction(PlayerVisualAction.Break);
        if (_blocks.GetDefinition(
                _world.GetCellOrEmpty(hit.Voxel).Block).Interaction ==
            BlockInteractionKind.Pickup)
            return;

        var held = _sessionStates.Player.Inventory.SelectedStack;
        if (_sessions.Active.ArtisansKit.IsEquipped(held))
        {
            if (TryUseArtisansKit(held, ToolUseHand.Left, hit))

            return;
        }
        if (_sessions.Active.Tools.IsSpecialLeftAction(held))
        {
            if (_sessions.Active.Tools.TryUse(held, ToolUseHand.Left, hit))

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

    }

    private MiningFeedbackEvent? AdvanceSurvivalMining()
    {
        if (!_worldReadySent || _player is not { IsBreakHeld: true } player ||
            !_sessionStates.Player.CanInteract ||
            _sessionStates.Player.GameMode != PlayerGameMode.Survival)
        {
            _sessions.Active.Mining.Cancel();
            PublishMiningProgress();
            return null;
        }

        var held = _sessionStates.Player.Inventory.SelectedStack;
        if (_sessions.Active.ArtisansKit.IsEquipped(held) ||
            _sessions.Active.Tools.IsSpecialLeftAction(held))
        {
            _sessions.Active.Mining.Cancel();
            PublishMiningProgress();
            return null;
        }

        var (from, to) = player.GetInteractionRay(InteractionDistance);
        var origin = new NVector3(from.X, from.Y, from.Z);
        var direction = new NVector3(
            to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        if (_sessions.Active.FindCreatureTarget(
                origin, direction, InteractionDistance) is not null ||
            CurrentObjectTarget() is not null)
        {
            _sessions.Active.Mining.Cancel();
            PublishMiningProgress();
            return null;
        }

        var inventory = _sessionStates.Player.Inventory;
        var mining = _sessions.Active.Mining;
        var previousFeedback = mining.FeedbackRevision;
        mining.Advance(
            CurrentTarget(), held, inventory.SelectedSlot,
            _worldTicks.TicksThisFrame, _sessionStates.Player.GameMode,
            _worldTicks.CurrentTick);
        MiningFeedbackEvent? feedback = null;
        if (mining.FeedbackRevision != previousFeedback)
        {
            _playerModel?.TryPlayAction(PlayerVisualAction.Break, fastBreak: true);
            feedback = mining.LastFeedback;
        }
        PublishMiningProgress();
        return feedback;
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

    private void TryTraversePhysicalPortal()
    {
        if (!_worldReadySent || _sessions.IsTransitioning ||
            _player is not { } player ||
            !_sessionStates.Player.CanInteract)
            return;

        var position = player.GlobalPosition;
        var at = new WorldVoxelCoord(
            Mathf.FloorToInt(position.X),
            Mathf.FloorToInt(position.Y),
            Mathf.FloorToInt(position.Z));

        if (!_sessions.Active.Portals.TryGetDestinationIntersecting(
                player.CollisionBounds, out var target))
        {
            _portalTravelLatched = false;
            return;
        }

        if (_portalTravelLatched || target == _dimension.Id.Value)
            return;

        var source = new NVector3(position.X, position.Y, position.Z);
        if (BeginDimensionTransition(
                new DimensionId(target), source,
                purpose: DimensionTravelPurpose.Portal))
        {
            _portalTravelLatched = true;
            _worldDiagnostics?.Write(
                $"portal.travel.begin destination={target} position={at}");
        }
    }

    private void PlaceTargetBlock()
    {
        if (!_sessionStates.Player.CanInteract || _player is null)
            return;

        _playerModel?.TryPlayAction(PlayerVisualAction.Place);
        if (CurrentObjectTarget() is { } objectHit)
        {
            if (_sessions.Active.ObjectInteractions.TryPickup(
                    objectHit, _sessionStates.Player.Inventory))
            {
                SyncHeldBlock();
                SendHotbarState();
                SendInventoryState();
                SendTargetHudState(force: true);
            }
            // Objects intercept interaction: do not place blocks behind them.
            return;
        }
        var selectedForPlacement = _sessionStates.Player.Inventory.SelectedStack;
        var fluidSurface = selectedForPlacement is { Kind: InventoryEntryKind.Object } &&
            _objects.TryGet(selectedForPlacement.Id, out var placingObject) &&
            placingObject?.SupportFluid is not null;
        var target = CurrentTarget(fluidSurface);
        if (target is { } storageHit &&
            _sessions.Active.TryOpenStorageBox(storageHit.Voxel))
        {
            _player.SuspendForModal();
            SendStorageBoxState();
            SendInventoryState();
            return;
        }

        var inventory = _sessionStates.Player.Inventory;
        var selected = inventory.SelectedStack;
        if (selected?.Kind == InventoryEntryKind.Object)
        {
            if (target is { } supportHit &&
                _sessions.Active.ObjectInteractions.TryPlace(
                    selected, supportHit))
            {
                if (_sessionStates.Player.GameMode == PlayerGameMode.Survival &&
                    !inventory.TryConsumeSelected())
                    throw new InvalidOperationException(
                        "Accepted Object placement lost selected stack.");
                SyncHeldBlock();
                SendHotbarState();
                SendInventoryState();
                SendTargetHudState(force: true);
            }
            return;
        }
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

        if (PortalActivationRuntime.IsSlicer(selected))
        {
            if (target is { } frameHit &&
                _sessions.Active.Portals.TryActivate(selected, frameHit,
                    out var portalTarget))
            {
                if (_sessionStates.Player.GameMode == PlayerGameMode.Survival &&
                    !inventory.TryConsumeSelected())
                    throw new InvalidOperationException(
                        "Successful portal activation lost selected Slicer.");

                _worldDiagnostics?.Write(
                    $"portal.activate destination={portalTarget} frame={frameHit.Voxel}");
                SyncHeldBlock();
                SendHotbarState();
                SendInventoryState();
            }
            return;
        }

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

            }
            return;
        }

        if (target is not { } hit)
            return;

        if (_sessions.Active.ArtisansKit.IsEquipped(selected))
        {
            if (TryUseArtisansKit(selected, ToolUseHand.Right, hit))

            return;
        }

        if (_sessions.Active.Tools.TryUse(
                selected, ToolUseHand.Right, hit))
        {

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
                _chat.AppendStructureFile(Path.GetFileName(file));
                _chatFeedbackSeconds = 10.0;
                SendChatState();
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

    private void SyncPlacementPreview()
    {
        if (_clientPreferences.HideHud || !_worldReadySent || _sessions.IsTransitioning ||
            _inventoryOpen || _brushPaletteOpen || _chat.IsOpen ||
            _player is not { IsMouseCaptured: true } player ||
            !_sessionStates.Player.CanInteract ||
            _sessionStates.Player.Inventory.SelectedStack is not { } selected ||
            CurrentTarget(selected.Kind == InventoryEntryKind.Object &&
                _objects.TryGet(selected.Id, out var previewDefinition) &&
                previewDefinition?.SupportFluid is not null) is not { } hit)
        {
            _placementPreview.Hide();
            _objectPlacementPreview.Hide();
            return;
        }

        if (selected.Kind == InventoryEntryKind.Object)
        {
            _placementPreview.Hide();
            // Match interaction interception: no ghost through closer Objects.
            var decision = CurrentObjectTarget() is not null
                ? default
                : ObjectPlacementResolver.Resolve(
                    _world, _blocks, _objects, selected, hit, _fluids);
            _objectPlacementPreview.Sync(decision, _objects,
                _sessions.Active.Generator.Tints);
            return;
        }

        _objectPlacementPreview.Hide();
        if (selected.Block is not { } block ||
            block.HasMicroblockGeometry)
        {
            _placementPreview.Hide();
            return;
        }

        var cell = _sessionStates.Player.HeldBlock.CurrentCell();
        if (cell.IsEmpty)
        {
            _placementPreview.Hide();
            return;
        }

        _placementPreview.Sync(BlockInteractionResolver.ResolvePlacement(
            _world, _blocks, hit, cell, player.CollisionBounds));
    }

    private void SyncArchitectsCompassPreview()
    {
        if (_clientPreferences.HideHud)
        {
            _sessions.Active.StructureSelection.Hide();
            return;
        }
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

    /// <summary>Object target wins only if it precedes the opaque block hit
    /// on the same ray. A closer block never allows picking through walls.</summary>
    private WorldObjectHit? CurrentObjectTarget()
    {
        if (_player is not { IsMouseCaptured: true } player ||
            _objects.Count == 0) return null;
        var (from, to) = player.GetInteractionRay(InteractionDistance);
        var origin = new NVector3(from.X, from.Y, from.Z);
        var direction = new NVector3(to.X - from.X, to.Y - from.Y, to.Z - from.Z);
        return WorldObjectTargetResolver.RaycastVisible(
            _world, _blocks, _objects, origin, direction, InteractionDistance);
    }

    private VoxelWorldHit? CurrentTarget(
        bool fluidSurface = false, bool includeFluidSource = false)
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
            InteractionDistance, fluidSurface, includeFluidSource);
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
            _worldDiagnostics?.Error("worker.fluid.failed", error);
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

        _worldDiagnostics?.ObserveFluid(
            report.WorkerMilliseconds, report.AppliedChangeCount,
            report.BacklogCount);
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
            _worldDiagnostics?.Error("worker.fluid_mesh.failed", error);
            GD.PushError(error.ToString());
            return;
        }

        if (report is not { } completed ||
            completed.Kind !=
                MeshPipelineCompletionKind.Applied)
        {
            return;
        }

        _worldDiagnostics?.ObserveFluidMesh(
            completed.WorkerMilliseconds, completed.Accepted, completed.Stale);
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
            _worldDiagnostics?.Error("worker.terrain_mesh.failed", error);
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
            completed.WorkerMilliseconds, completed.Accepted, completed.Stale,
            completed.CaptureMilliseconds);
        if (_debugHudVisible)
        {
            GD.Print(
                $"world.geometry worker_ms=" +
                $"{completed.WorkerMilliseconds:F2} " +
                $"accepted={completed.Accepted} " +
                $"stale={completed.Stale}");
        }
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
            _worldDiagnostics?.Error("worker.lighting.failed", error);
            GD.PushError(error.ToString());
            return;
        }

        if (report is null)
        {
            return;
        }

        _worldDiagnostics?.ObserveLighting(report);
        if (report.Kind != LightingCompletionKind.Applied)
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
                $"{report.ProcessedVoxelCount} " +
                $"capture_ms={report.CaptureMilliseconds:F2} " +
                $"apply_ms={report.ApplyMilliseconds:F2}");
        }

    }

    private void IntegrateMeshletPublications()
    {
        var stats =
            _terrainMeshPipeline.IntegratePublications(
                MaxMeshletPublishesPerFrame,
                _worldFrameBudget);
        _worldDiagnostics?.ObserveTerrainPublication(stats);

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

    private void DispatchOneWorldWorker(
        bool underStreamingPressure, ChunkCoord center)
    {
        // A worker captures its immutable neighborhood synchronously.
        // Start at most one snapshot per frame and rotate across owners.
        for (var offset = 0; offset < 4; offset++)
        {
            var lane = (_nextWorldWorker + offset) % 4;
            var started = lane switch
            {
                0 => _fluidSimulationRuntime.TryStartReadyWork(),
                1 => _terrainMeshPipeline.TryStartReadyWork(
                    underStreamingPressure, center, _residency.MovementDirection),
                2 => _lightingRuntime.TryStartReadyWork(),
                _ => _fluidMeshPipeline.TryStartReadyWork(),
            };
            if (!started)
                continue;

            _nextWorldWorker = (lane + 1) % 4;
            return;
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
