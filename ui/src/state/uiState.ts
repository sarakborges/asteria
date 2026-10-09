import type { WorldSummaryView } from "../presentation/worldCatalogModels";
import type { ChatMessageView } from "../presentation/chatModels";

export type BridgeStatusTone =
  | "neutral"
  | "connecting"
  | "connected";

export type HotbarSlotState = {
  id?: string;
  quantity?: number;
  kind?: "block" | "item" | "tool" | "layer";
  metadata?: Record<string, string>;
  iconUrl?: string;
};

export type HotbarState = {
  slots: HotbarSlotState[];
  selectedIndex: number | null;
  selectedName: string | null;
};

export type WorldBannerState = {
  sphere: string;
  biome: string;
  x: number;
  y: number;
  z: number;
  heading: number;
};

export type VitalValue = {
  current: number;
  maximum: number;
};

export type PlayerVitalsState = {
  health?: VitalValue | null;
  stamina?: VitalValue | null;
};

export type HudEntityState = {
  name: string;
  health: VitalValue;
  portraitUrl?: string;
};

export type TargetHudState = {
  kind: "block" | "object" | "fluid";
  id: string;
  name: string;
  details: string[];
};

export type WorldClockState = {
  day: number;
  hour: number;
  minute: number;
};

export type StatusEffectTone =
  | "positive"
  | "negative"
  | "neutral";

export type StatusEffectState = {
  id: string;
  label: string;
  duration?: string;
  tone: StatusEffectTone;
};

export type InteractionPromptState = {
  key: string;
  text: string;
} | null;

export type ToastTone =
  | "info"
  | "success"
  | "warning";

export type ToastState = {
  id: number;
  message: string;
  tone: ToastTone;
  durationMs?: number;
};

export type ToastInput =
  Omit<ToastState, "id">;

export type StatusCardState = {
  bridgeLabel: string;
  bridgeTone: BridgeStatusTone;
  worldStatus: string;
  playerStatus: string;
  lastMessage: string | null;
};

export type HudState = {
  targetPosition?: "Center" | "TopRight" | "Hidden";
  gameMode?: "survival" | "creative" | "spectator";
  debugVisible: boolean;
  hotbar: HotbarState;
  world: WorldBannerState | null;
  vitals: PlayerVitalsState | null;
  playerPortraitUrl: string | null;
  target: TargetHudState | null;
  miningProgress: number | null;
  artisansKitResolution: 1 | 2 | 4 | null;
  targetEntity: HudEntityState | null;
  clock: WorldClockState | null;
  fps: number | null;
  effects: StatusEffectState[];
  prompt: InteractionPromptState;
  toasts: ToastState[];
  statusCard: StatusCardState;
};

export type LoadingPhase =
  | "retiring_current_dimension"
  | "materializing_initial_area"
  | "preparing_presentation"
  | "preparing_world";

export type LoadingState = {
  phase: LoadingPhase;
  completed: number;
  total: number;
  dimension: string;
};

export type WorldCreationErrorKey =
  | "newWorld.error.seedMustBeString"
  | "newWorld.error.invalidSeed"
  | "newWorld.error.invalidName"
  | "newWorld.error.invalidMode"
  | "newWorld.error.invalidTickRate"
  | "newWorld.error.invalidGeneration"
  | "newWorld.error.unexpected";

export type BrushPaletteColor = { id: string; rgb: string };
export type BrushPaletteState = {
  selectedId: string | null;
  colors: BrushPaletteColor[];
};

export type GameMode = "Survival" | "Creative" | "Spectator";
export type OverlayScreen = "none" | "pause" | "game" | "world" | "controls" | "inventory" | "brush" | "storage";

export type WorldSettingsState = {
  name: string; mode: GameMode; ticksPerSecond: number; spawnCreatures: boolean;
};
export type ClientSettingsState = {
  renderDistanceChunks: number;
  hud: {
    hideHints: boolean;
    targetBlockPosition: "Center" | "TopRight" | "Hidden";
    hints: {
      rotateBlock: boolean;
      breakOrPlaceBlock: boolean;
    };
  };
  keybinds: {
    jump: string; descend: string; toolAction: string;
    inventory: string; chat: string; dropItem: string;
    changePerspective: string;
  };
};
export type SettingsState = {
  client: ClientSettingsState | null;
  world: WorldSettingsState | null;
  captureAction: "Jump" | "Descend" | "ToolAction" | "Inventory" | "Chat" | "DropItem" | "ChangePerspective" | null;
  errorKey: string | null;
};

export type WorldGenerationMode = "Normal" | "Flat" | "Void";
export type SpawnBiomeOption = { id: string; label: string };
export type WorldGenerationDraft = {
  mode: WorldGenerationMode;
  spawnBiome: string | null;
  biomeSizeTenths: number;
  spawnStructures: boolean;
  singleBiome: boolean;
  spawnCaves: boolean;
  spawnOceans: boolean;
};

export type WorldCreationState = {
  visible: boolean;
  seed: string;
  name: string;
  mode: GameMode;
  ticksPerSecond: string;
  spawnCreatures: boolean;
  generation: WorldGenerationDraft;
  spawnBiomes: SpawnBiomeOption[];
  pending: boolean;
  generating: boolean;
  errorKey: WorldCreationErrorKey | null;
};

export type InventoryEntryKind = "block" | "item" | "tool" | "layer";
export type InventoryMetadata = Record<string, string>;

export type InventorySlotState = {
  id: string;
  kind: InventoryEntryKind;
  quantity: number;
  metadata: InventoryMetadata;
} | null;

export type InventoryCatalogEntry = {
  id: string;
  kind: InventoryEntryKind;
  name: string;
  category: string;
  metadata: InventoryMetadata;
  iconUrl?: string;
};

export type StorageBoxState = {
  open: boolean;
  position: { x: number; y: number; z: number } | null;
  slots: InventorySlotState[];
  errorKey: string | null;
};

export type InventoryCraftingRecipe = {
  id: string;
  resultId: string;
  outputQuantity: number;
  ingredients: { id: string; required: number; available: number }[];
  craftable: boolean;
};

export type InventoryCraftingStatus =
  | { code: "Crafted"; recipeId: string }
  | { code: "UnknownRecipe" | "MissingIngredients" | "InventoryFull"; recipeId: string };

export type GameplayInventoryState = {
  open: boolean;
  creativeAvailable: boolean;
  selectedIndex: number;
  backpack: InventorySlotState[];
  hotbar: InventorySlotState[];
  cursor: InventorySlotState;
  catalog: InventoryCatalogEntry[];
  portraitUrl: string | null;
  recipes: InventoryCraftingRecipe[];
  craftingStatus: InventoryCraftingStatus | null;
  errorKey: string | null;
};

export type UiNavigationState = {
  preWorldScreen: "starting" | "world-selection" | "new-world";
  overlay: OverlayScreen;
  saveFeedback: "saved" | "error" | null;
};

export type WorldCatalogState = {
  status: "unavailable" | "verifying" | "ready" | "error";
  worlds: readonly WorldSummaryView[];
  folderError: boolean;
};

export type ChatCompletionCatalog = {
  creatures: readonly string[];
  biomes: readonly string[];
  structures: readonly string[];
  variations: Readonly<Record<string, readonly string[]>>;
  dimensions: readonly string[];
  position: { x: number; y: number; z: number } | null;
};

export type ChatState = {
  open: boolean;
  visible: boolean;
  history: readonly ChatMessageView[];
  commands: readonly string[];
  catalog: ChatCompletionCatalog;
};

export type UiState = {
  mouseCaptured: boolean;
  hud: HudState;
  loading: LoadingState | null;
  worldCreation: WorldCreationState;
  worldCatalog: WorldCatalogState;
  chat: ChatState;
  navigation: UiNavigationState;
  settings: SettingsState;
  inventory: GameplayInventoryState;
  storageBox: StorageBoxState;
  brush: BrushPaletteState;
};

export function createInitialUiState(
  embedded: boolean,
): UiState {
  return {
    mouseCaptured: false,
    hud: {
      targetPosition: "Center",
      gameMode: "survival",
      debugVisible: false,
      hotbar: {
        slots: [],
        selectedIndex: null,
        selectedName: null,
      },
      world: null,
      vitals: null,
      playerPortraitUrl: null,
      target: null,
      miningProgress: null,
      artisansKitResolution: null,
      targetEntity: null,
      clock: null,
      fps: null,
      effects: [],
      prompt: null,
      toasts: [],
      statusCard: {
        bridgeLabel: embedded
          ? "debug.bridge.connecting"
          : "debug.bridge.browser",
        bridgeTone: embedded
          ? "connecting"
          : "neutral",
        worldStatus: "debug.world.waiting",
        playerStatus: "debug.player.waiting",
        lastMessage: null,
      },
    },
    loading: null,
    worldCatalog: { status: "unavailable", worlds: [], folderError: false },
    chat: {
      open: false, visible: false, history: [], commands: [],
      catalog: {
        creatures: [], biomes: [], structures: [], variations: {},
        dimensions: [], position: null,
      },
    },
    worldCreation: {
      visible: true,
      seed: "",
      name: "New World",
      mode: "Survival",
      ticksPerSecond: "40",
      spawnCreatures: true,
      generation: {
        mode: "Normal", spawnBiome: null, biomeSizeTenths: 10,
        spawnStructures: true, singleBiome: false,
        spawnCaves: true, spawnOceans: true,
      },
      spawnBiomes: [],
      pending: true,
      generating: false,
      errorKey: null,
    },
    navigation: {
      preWorldScreen: "starting",
      overlay: "none",
      saveFeedback: null,
    },
    settings: {
      client: null, world: null,
      captureAction: null, errorKey: null,
    },
    brush: {
      selectedId: null, colors: [],
    },
    storageBox: { open: false, position: null, slots: Array(27).fill(null), errorKey: null },
    inventory: {
      open: false,
      creativeAvailable: false,
      selectedIndex: 0,
      backpack: Array(27).fill(null),
      hotbar: Array(9).fill(null),
      cursor: null,
      catalog: [],
      portraitUrl: null,
      recipes: [],
      craftingStatus: null,
      errorKey: null,
    },
  };
}
