import type { BridgeMessage } from "../bridge/godotBridge";
import type { ClientSettingsState, GameMode, WorldSettingsState, SettingsState } from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

export function createSettingsController(
  store: UiStore,
  post: (type: string, payload?: unknown) => void,
) {
  const change = (patch: Partial<SettingsState>) =>
    store.update(state => ({
      ...state, settings: { ...state.settings, ...patch },
    }));

  return {
    setRenderDistance(value: number) {
      post("ui.client_preferences.render_distance", { value });
    },
    setTargetPosition(value: "Center" | "TopRight" | "Hidden") {
      post("ui.client_preferences.target_block_position", { value });
    },
    setHideHints(value: boolean) {
      post("ui.client_preferences.hide_hints", { value });
    },
    setGameplayHint(kind: "RotateBlock" | "BreakOrPlaceBlock", value: boolean) {
      post("ui.client_preferences.hint", { kind, value });
    },
    setWorldTicks(value: number) {
      post("ui.world.set_ticks", { value });
    },
    setGameMode(value: GameMode) {
      post("ui.player.set_game_mode", { mode: value });
    },
    beginKeyCapture(action: "Jump" | "Descend") {
      post("ui.client_preferences.capture_keybind", { action });
    },
    cancelKeyCapture() {
      post("ui.client_preferences.cancel_key_capture");
    },
    handleGodotMessage(message: BridgeMessage) {
      const payload = asRecord(message.payload);
      switch (message.type) {
        case "game.client_preferences": {
          const hud = asRecord(payload?.hud);
          const keys = asRecord(payload?.keybinds);
          const position = hud?.targetBlockPosition;
          const hints = asRecord(hud?.hints);
          if (!payload || !hud || !keys || !hints ||
              typeof hints.rotateBlock !== "boolean" ||
              typeof hints.breakOrPlaceBlock !== "boolean" ||
              typeof payload.renderDistanceChunks !== "number" ||
              !["Center", "TopRight", "Hidden"].includes(String(position)) ||
              typeof keys.jump !== "string" || typeof keys.descend !== "string") break;
          const client: ClientSettingsState = {
            renderDistanceChunks: payload.renderDistanceChunks,
            hud: {
              hideHints: hud.hideHints === true,
              targetBlockPosition: position as ClientSettingsState["hud"]["targetBlockPosition"],
              hints: {
                rotateBlock: hints.rotateBlock,
                breakOrPlaceBlock: hints.breakOrPlaceBlock,
              },
            },
            keybinds: { jump: keys.jump, descend: keys.descend },
          };
          store.update(state => ({
            ...state,
            settings: { ...state.settings, client },
            hud: { ...state.hud, targetPosition: client.hud.targetBlockPosition },
          }));
          break;
        }
        case "game.world_settings": {
          if (!payload || typeof payload.name !== "string" ||
              typeof payload.ticksPerSecond !== "number" ||
              !["Survival", "Creative", "Spectator"].includes(String(payload.mode))) break;
          const world: WorldSettingsState = {
            name: payload.name,
            ticksPerSecond: payload.ticksPerSecond,
            mode: payload.mode as GameMode,
          };
          change({ world, errorKey: null });
          break;
        }
        case "game.client_preferences.key_capture": {
          const action = payload?.action;
          const status = payload?.status;
          const isActive = ["Capturing", "ReservedKey", "KeyConflict", "UnsupportedKey"].includes(String(status));
          const captureAction = isActive &&
            (action === "Jump" || action === "Descend") ? action : null;
          const errors: Record<string, string> = {
            ReservedKey: "settings.error.reservedKey",
            KeyConflict: "settings.error.keyConflict",
            UnsupportedKey: "settings.error.unsupportedKey",
            SaveFailed: "settings.error.saveFailed",
            InvalidRequest: "settings.error.invalidValue",
          };
          change({
            captureAction,
            errorKey: typeof status === "string" ? errors[status] ?? null : null,
          });
          break;
        }
        case "game.player_mode.error":
        case "game.world_settings.error":
        case "game.client_preferences.error": {
          const errors: Record<string, string> = {
            NoSafeCollisionSpace: "settings.error.noSafeCollisionSpace",
            InvalidTickRate: "newWorld.error.invalidTickRate",
            InvalidValue: "settings.error.invalidValue",
            SaveFailed: "settings.error.saveFailed",
          };
          change({ errorKey: errors[String(payload?.code)] ?? "settings.error.invalidValue" });
          break;
        }
      }
    },
  };
}
