import type { BridgeMessage } from "../bridge/godotBridge";
import type { WorldCreationErrorKey, WorldCreationState } from "../state/uiState";
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
        errorKey: null,
      });
      postMessage("ui.world.create", {
        seed: seed.trim(),
      });
    },

    randomizeWorld() {
      updateWorldCreation(store, {
        pending: true,
        errorKey: null,
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
              errorKey: null,
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
            errorKey: readErrorKey(payload?.key),
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
            errorKey: null,
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

function readErrorKey(value: unknown): WorldCreationErrorKey {
  switch (value) {
    case "newWorld.error.seedMustBeString":
    case "newWorld.error.invalidSeed":
      return value;
    default:
      return "newWorld.error.unexpected";
  }
}
