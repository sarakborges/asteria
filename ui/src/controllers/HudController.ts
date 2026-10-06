import type { BridgeMessage } from "../bridge/godotBridge";
import type { GameHudPageView } from "../components/pages/GameHudPage";
import type { StatusEffectState } from "../components/organisms/StatusEffects";
import { applyUiTheme } from "../theme/uiTheme";

export type HudController = {
  mount(): void;
  handleGodotMessage(message: BridgeMessage): void;
};

export function createHudController(
  view: GameHudPageView,
  postMessage: (type: string, payload?: unknown) => void,
): HudController {
  let debugVisible = false;

  const handleContextMenu = (event: Event): void => {
    event.preventDefault();
  };

  const handleKeyDown = (event: KeyboardEvent): void => {
    if (event.key !== "F3") return;
    event.preventDefault();
    debugVisible = !debugVisible;
    view.shell.setDebugVisible(debugVisible);
  };

  const handlePing = (): void => {
    view.statusCard.lastMessage.textContent =
      "webui → godot: ping sent";
    postMessage("ui.ping");
  };

  return {
    mount() {
      document.addEventListener("contextmenu", handleContextMenu);
      document.addEventListener("keydown", handleKeyDown);
      view.statusCard.pingButton.addEventListener("click", handlePing);
    },

    handleGodotMessage(message) {
      view.statusCard.lastMessage.textContent =
        `godot → webui: ${message.type}`;

      switch (message.type) {
        case "game.ui_theme":
          applyUiTheme(message.payload);
          break;

        case "game.hud.hotbar":
          applyHotbar(view, message.payload);
          break;

        case "game.hud.vitals":
          applyVitals(view, message.payload);
          break;

        case "game.hud.effects":
          applyEffects(view, message.payload);
          break;

        case "game.hud.prompt":
          applyPrompt(view, message.payload);
          break;

        case "game.hud.toast":
          applyToast(view, message.payload);
          break;

        case "game.ready":
          view.statusCard.bridgeStatus.setLabel("bridge connected");
          view.statusCard.bridgeStatus.setTone("connected");
          break;

        case "game.chunk_ready":
          view.statusCard.worldStatus.textContent =
            "chunk generated + collision ready";
          break;

        case "game.player_ready":
          view.statusCard.playerStatus.textContent =
            "FPS controller ready";
          break;

        case "game.mouse_capture": {
          const payload = asRecord(message.payload);
          document.documentElement.classList.toggle(
            "mouse-captured",
            payload?.captured === true,
          );
          break;
        }

        case "game.pong":
          view.statusCard.lastMessage.textContent =
            "godot → webui: pong received";
          break;
      }
    },
  };
}

function applyHotbar(view: GameHudPageView, payload: unknown): void {
  const value = asRecord(payload);
  if (!value) return;

  const slots = Array.isArray(value.slots)
    ? value.slots.map((slot) => {
        const item = asRecord(slot);
        return {
          id: item && typeof item.id === "string" ? item.id : undefined,
          quantity:
            item && typeof item.quantity === "number"
              ? item.quantity
              : undefined,
        };
      })
    : [];

  view.hotbar.setState({
    slots,
    selectedIndex:
      typeof value.selectedIndex === "number" ? value.selectedIndex : null,
    selectedName:
      typeof value.selectedName === "string" ? value.selectedName : null,
  });
}

function applyVitals(view: GameHudPageView, payload: unknown): void {
  const value = asRecord(payload);
  if (!value) {
    view.playerVitals.setState(null);
    return;
  }

  const health = readVital(value.health);
  const stamina = readVital(value.stamina);
  view.playerVitals.setState(
    health || stamina ? { health, stamina } : null,
  );
}

function applyEffects(view: GameHudPageView, payload: unknown): void {
  const value = asRecord(payload);
  if (!value || !Array.isArray(value.effects)) {
    view.statusEffects.setEffects([]);
    return;
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
      tone:
        effect.tone === "positive" || effect.tone === "negative"
          ? effect.tone
          : "neutral",
    });
  }

  view.statusEffects.setEffects(effects);
}

function applyPrompt(view: GameHudPageView, payload: unknown): void {
  const value = asRecord(payload);
  if (
    !value ||
    typeof value.key !== "string" ||
    typeof value.text !== "string"
  ) {
    view.interactionPrompt.setPrompt(null);
    return;
  }

  view.interactionPrompt.setPrompt({
    key: value.key,
    text: value.text,
  });
}

function applyToast(view: GameHudPageView, payload: unknown): void {
  const value = asRecord(payload);
  if (!value || typeof value.message !== "string") return;

  view.toasts.push({
    message: value.message,
    tone:
      value.tone === "success" || value.tone === "warning"
        ? value.tone
        : "info",
    durationMs:
      typeof value.durationMs === "number"
        ? value.durationMs
        : undefined,
  });
}

function readVital(
  value: unknown,
): { current: number; maximum: number } | null {
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

function asRecord(value: unknown): Record<string, unknown> | null {
  return typeof value === "object" &&
    value !== null &&
    !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}
