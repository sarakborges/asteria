import type { BridgeMessage } from "../bridge/godotBridge";
import type { WorldCreationState } from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

export type WorldCreationController = {
  createWorld(seed: string): void;
  randomizeWorld(): void;
  handleGodotMessage(message: BridgeMessage): void;
};

export function createWorldCreationController(
  store: UiStore,
  postMessage: (type: string, payload?: unknown) => void,
): WorldCreationController {
  return {
    createWorld(seed) {
      updateWorldCreation(store, {
        pending: true,
        error: null,
      });
      postMessage("ui.world.create", {
        seed: seed.trim(),
      });
    },

    randomizeWorld() {
      updateWorldCreation(store, {
        pending: true,
        error: null,
      });
      postMessage("ui.world.randomize");
    },

    handleGodotMessage(message) {
      switch (message.type) {
        case "game.world_creation": {
          const payload = asRecord(message.payload);
          if (payload && typeof payload.seed === "string") {
            updateWorldCreation(store, {
              visible: true,
              seed: payload.seed,
              pending: false,
              generating: false,
              error: null,
            });
          }
          break;
        }

        case "game.world_creation.error": {
          const payload = asRecord(message.payload);
          updateWorldCreation(store, {
            visible: true,
            pending: false,
            generating: false,
            error:
              payload && typeof payload.message === "string"
                ? payload.message
                : "Não foi possível criar o mundo.",
          });
          break;
        }

        case "game.world_creation.started": {
          const payload = asRecord(message.payload);
          updateWorldCreation(store, {
            seed:
              payload && typeof payload.seed === "string"
                ? payload.seed
                : store.getSnapshot().worldCreation.seed,
            pending: true,
            generating: true,
            error: null,
          });
          break;
        }

        case "game.chunk_ready":
          updateWorldCreation(store, {
            visible: false,
          });
          break;
      }
    },
  };
}

function updateWorldCreation(
  store: UiStore,
  patch: Partial<WorldCreationState>,
): void {
  store.update((state) => ({
    ...state,
    worldCreation: {
      ...state.worldCreation,
      ...patch,
    },
  }));
}
