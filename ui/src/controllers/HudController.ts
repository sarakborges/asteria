import type { BridgeMessage } from "../bridge/godotBridge";
import type {
  HotbarState,
  HudState,
  InteractionPromptState,
  PlayerVitalsState,
  StatusEffectState,
  StatusEffectTone,
  ToastTone,
  VitalValue,
  WorldBannerState,
} from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { applyUiTheme } from "../theme/uiTheme";
import {
  asRecord,
  isFiniteNumber,
} from "./messagePayload";

export type HudController = {
  ping(): void;
  handleGodotMessage(message: BridgeMessage): void;
};

export function createHudController(
  store: UiStore,
  postMessage: (type: string, payload?: unknown) => void,
): HudController {
  return {
    ping() {
      patchStatusCard(store, {
        lastMessage: "webui → godot: ping sent",
      });
      postMessage("ui.ping");
    },

    handleGodotMessage(message) {
      patchStatusCard(store, {
        lastMessage: "godot → webui: " + message.type,
      });

      switch (message.type) {
        case "game.ui_theme":
          applyUiTheme(message.payload);
          break;

        case "game.hud.debug": {
          const payload = asRecord(message.payload);
          patchHud(store, {
            debugVisible: payload?.visible === true,
          });
          break;
        }

        case "game.hud.hotbar":
          patchHud(store, {
            hotbar: readHotbar(message.payload),
          });
          break;

        case "game.hud.world":
          patchHud(store, {
            world: readWorld(message.payload),
          });
          break;

        case "game.hud.vitals":
          patchHud(store, {
            vitals: readVitals(message.payload),
          });
          break;

        case "game.hud.effects":
          patchHud(store, {
            effects: readEffects(message.payload),
          });
          break;

        case "game.hud.prompt":
          patchHud(store, {
            prompt: readPrompt(message.payload),
          });
          break;

        case "game.hud.toast": {
          const toast = readToast(message.payload);
          if (toast) {
            store.pushToast(toast);
          }
          break;
        }

        case "game.ready":
          patchStatusCard(store, {
            bridgeLabel: "bridge connected",
            bridgeTone: "connected",
          });
          break;

        case "game.chunk_ready":
          patchStatusCard(store, {
            worldStatus: "chunk generated + collision ready",
          });
          break;

        case "game.player_ready":
          patchStatusCard(store, {
            playerStatus: "FPS controller ready",
          });
          break;

        case "game.mouse_capture": {
          const payload = asRecord(message.payload);
          store.update((state) => ({
            ...state,
            mouseCaptured: payload?.captured === true,
          }));
          break;
        }

        case "game.pong":
          patchStatusCard(store, {
            lastMessage: "godot → webui: pong received",
          });
          break;
      }
    },
  };
}

function patchHud(
  store: UiStore,
  patch: Partial<HudState>,
): void {
  store.update((state) => ({
    ...state,
    hud: {
      ...state.hud,
      ...patch,
    },
  }));
}

function patchStatusCard(
  store: UiStore,
  patch: Partial<HudState["statusCard"]>,
): void {
  store.update((state) => ({
    ...state,
    hud: {
      ...state.hud,
      statusCard: {
        ...state.hud.statusCard,
        ...patch,
      },
    },
  }));
}

function readHotbar(payload: unknown): HotbarState {
  const value = asRecord(payload);
  if (!value) {
    return {
      slots: [],
      selectedIndex: null,
      selectedName: null,
    };
  }

  const slots = Array.isArray(value.slots)
    ? value.slots.map((raw) => {
        const slot = asRecord(raw);
        return {
          id:
            slot && typeof slot.id === "string"
              ? slot.id
              : undefined,
          quantity:
            slot && typeof slot.quantity === "number"
              ? slot.quantity
              : undefined,
        };
      })
    : [];

  return {
    slots,
    selectedIndex:
      typeof value.selectedIndex === "number"
        ? value.selectedIndex
        : null,
    selectedName:
      typeof value.selectedName === "string"
        ? value.selectedName
        : null,
  };
}

function readWorld(
  payload: unknown,
): WorldBannerState | null {
  const value = asRecord(payload);
  if (
    !value ||
    typeof value.sphere !== "string" ||
    typeof value.biome !== "string" ||
    !isFiniteNumber(value.x) ||
    !isFiniteNumber(value.y) ||
    !isFiniteNumber(value.z) ||
    !isFiniteNumber(value.heading)
  ) {
    return null;
  }

  return {
    sphere: value.sphere,
    biome: value.biome,
    x: value.x,
    y: value.y,
    z: value.z,
    heading: value.heading,
  };
}

function readVitals(
  payload: unknown,
): PlayerVitalsState | null {
  const value = asRecord(payload);
  if (!value) return null;

  const health = readVital(value.health);
  const stamina = readVital(value.stamina);

  return health || stamina
    ? {
        health,
        stamina,
      }
    : null;
}

function readVital(value: unknown): VitalValue | null {
  const item = asRecord(value);
  if (
    !item ||
    typeof item.current !== "number" ||
    typeof item.maximum !== "number"
  ) {
    return null;
  }

  return {
    current: item.current,
    maximum: item.maximum,
  };
}

function readEffects(
  payload: unknown,
): StatusEffectState[] {
  const value = asRecord(payload);
  if (!value || !Array.isArray(value.effects)) {
    return [];
  }

  const effects: StatusEffectState[] = [];
  for (const rawEffect of value.effects) {
    const effect = asRecord(rawEffect);
    if (
      !effect ||
      typeof effect.id !== "string" ||
      typeof effect.label !== "string"
    ) {
      continue;
    }

    effects.push({
      id: effect.id,
      label: effect.label,
      duration:
        typeof effect.duration === "string"
          ? effect.duration
          : undefined,
      tone: readEffectTone(effect.tone),
    });
  }

  return effects;
}

function readEffectTone(value: unknown): StatusEffectTone {
  return value === "positive" || value === "negative"
    ? value
    : "neutral";
}

function readPrompt(
  payload: unknown,
): InteractionPromptState {
  const value = asRecord(payload);
  return value &&
    typeof value.key === "string" &&
    typeof value.text === "string"
    ? {
        key: value.key,
        text: value.text,
      }
    : null;
}

function readToast(
  payload: unknown,
): {
  message: string;
  tone: ToastTone;
  durationMs?: number;
} | null {
  const value = asRecord(payload);
  if (!value || typeof value.message !== "string") {
    return null;
  }

  return {
    message: value.message,
    tone:
      value.tone === "success" || value.tone === "warning"
        ? value.tone
        : "info",
    durationMs:
      typeof value.durationMs === "number"
        ? value.durationMs
        : undefined,
  };
}
