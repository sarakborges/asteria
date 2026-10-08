import type { BridgeMessage } from "../bridge/godotBridge";
import type { WorldCreationErrorKey, WorldCreationState, GameMode, WorldGenerationDraft, SpawnBiomeOption } from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

export type WorldCreationController = {
  createWorld(request: {
    seed: string; name: string; mode: GameMode; ticksPerSecond: string;
    spawnCreatures: boolean;
    generation: WorldGenerationDraft;
  }): void;
  randomizeWorld(): void;
  handleGodotMessage(message: BridgeMessage): void;
};

export function createWorldCreationController(
  store: UiStore,
  postMessage: (type: string, payload?: unknown) => void,
): WorldCreationController {
  return {
    createWorld(request) {
      const ticks = request.ticksPerSecond.trim();
      if (!/^[1-9][0-9]*$/.test(ticks) ||
          !Number.isSafeInteger(Number(ticks)) ||
          Number(ticks) > 4294967295) {
        updateWorldCreation(store, { errorKey: "newWorld.error.invalidTickRate" });
        return;
      }
      const generation = request.generation;
      const eligible = store.getSnapshot().worldCreation.spawnBiomes;
      const biomeValid = generation.spawnBiome === null ||
        eligible.some(entry => entry.id === generation.spawnBiome);
      if (!biomeValid || (generation.singleBiome && generation.spawnBiome === null) ||
          !Number.isInteger(generation.biomeSizeTenths) ||
          generation.biomeSizeTenths < 5 || generation.biomeSizeTenths > 50 ||
          !["Normal", "Flat", "Void"].includes(generation.mode)) {
        updateWorldCreation(store, { errorKey: "newWorld.error.invalidGeneration" });
        return;
      }
      updateWorldCreation(store, { pending: true, errorKey: null });
      postMessage("ui.world.create", {
        seed: request.seed.trim(), name: request.name, mode: request.mode,
        ticksPerSecond: Number(ticks),
        spawnCreatures: request.spawnCreatures,
        generation,
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
              spawnBiomes: readSpawnBiomes(payload.spawnBiomes),
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
    case "newWorld.error.invalidName":
    case "newWorld.error.invalidMode":
    case "newWorld.error.invalidTickRate":
    case "newWorld.error.invalidGeneration":
      return value;
    default:
      return "newWorld.error.unexpected";
  }
}

function readSpawnBiomes(raw: unknown): SpawnBiomeOption[] {
  if (!Array.isArray(raw)) return [];
  return raw.flatMap(option => {
    const value = asRecord(option);
    return value && typeof value.id === "string" &&
        typeof value.label === "string"
      ? [{ id: value.id, label: value.label }]
      : [];
  });
}
