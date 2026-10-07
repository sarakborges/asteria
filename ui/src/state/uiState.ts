export type BridgeStatusTone =
  | "neutral"
  | "connecting"
  | "connected";

export type HotbarSlotState = {
  id?: string;
  quantity?: number;
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
  lastMessage: string;
};

export type HudState = {
  debugVisible: boolean;
  hotbar: HotbarState;
  world: WorldBannerState | null;
  vitals: PlayerVitalsState | null;
  effects: StatusEffectState[];
  prompt: InteractionPromptState;
  toasts: ToastState[];
  statusCard: StatusCardState;
};

export type LoadingState = {
  phaseLabel: string;
  completed: number;
  total: number;
  dimension: string;
};

export type WorldCreationState = {
  visible: boolean;
  seed: string;
  pending: boolean;
  generating: boolean;
  error: string | null;
};

export type UiNavigationState = {
  preWorldScreen:
    | "starting"
    | "new-world";
};

export type UiState = {
  mouseCaptured: boolean;
  hud: HudState;
  loading: LoadingState | null;
  worldCreation: WorldCreationState;
  navigation: UiNavigationState;
};

export function createInitialUiState(
  embedded: boolean,
): UiState {
  return {
    mouseCaptured: false,
    hud: {
      debugVisible: false,
      hotbar: {
        slots: [],
        selectedIndex: null,
        selectedName: null,
      },
      world: null,
      vitals: null,
      effects: [],
      prompt: null,
      toasts: [],
      statusCard: {
        bridgeLabel: embedded
          ? "connecting to Godot"
          : "standalone browser mode",
        bridgeTone: embedded
          ? "connecting"
          : "neutral",
        worldStatus: "waiting for chunk",
        playerStatus: "waiting for player",
        lastMessage: "no bridge messages yet",
      },
    },
    loading: null,
    worldCreation: {
      visible: true,
      seed: "",
      pending: true,
      generating: false,
      error: null,
    },
    navigation: {
      preWorldScreen: "starting",
    },
  };
}
