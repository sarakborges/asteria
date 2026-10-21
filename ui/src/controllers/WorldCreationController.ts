import type { BridgeMessage } from "../bridge/godotBridge";
import type { WorldCreationErrorKey, WorldCreationState, GameMode, CreatureAttackMode, WorldGenerationDraft, SpawnBiomeOption } from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

export type WorldCreationController = {
  createWorld(request: {
    seed: string; name: string; mode: GameMode; ticksPerSecond: string;
    spawnCreatures: boolean;
    keepInventory: boolean;
    creatureAttackMode: CreatureAttackMode;
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
      // The native Create intent is single-flight. Footer clicks, Enter and
      // WebView event forwarding must not publish duplicate world requests.
      if (store.getSnapshot().worldCreation.pending) return;
      const ticks = request.ticksPerSecond.trim();
      if (!/^[1-9][0-9]*$/.test(ticks) ||
          !Number.isSafeInteger(Number(ticks)) ||
          Number(ticks) > 4294967295) {
        updateWorldCreation(store, { errorKey: "newWorld.error.invalidTickRate" });
        return;
      }
      if (request.creatureAttackMode !== "Default" && request.creatureAttackMode !== "Peaceful") {
        updateWorldCreation(store, { errorKey: "newWorld.error.invalidCreatureAttackMode" });
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
        keepInventory: request.keepInventory,
        creatureAttackMode: request.creatureAttackMode,
        generation,
      });
    },

    randomizeWorld() {
      if (store.getSnapshot().worldCreation.pending) return;
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
            const isDraft = payload.isRecreationDraft === true;
            const rawGeneration = asRecord(payload.generation);
            const current = store.getSnapshot().worldCreation;
            const validMode = (value: unknown): value is GameMode =>
              value === "Survival" || value === "Creative" || value === "Spectator";
            const validAttackMode = (value: unknown): value is CreatureAttackMode =>
              value === "Default" || value === "Peaceful";
            const validGenerationMode = (value: unknown): value is WorldGenerationDraft["mode"] =>
              value === "Normal" || value === "Flat" || value === "Void";
            const generation = rawGeneration &&
                validGenerationMode(rawGeneration.mode) &&
                (rawGeneration.spawnBiome === null || typeof rawGeneration.spawnBiome === "string") &&
                typeof rawGeneration.biomeSizeTenths === "number" &&
                typeof rawGeneration.spawnStructures === "boolean" &&
                typeof rawGeneration.singleBiome === "boolean" &&
                typeof rawGeneration.spawnCaves === "boolean" &&
                typeof rawGeneration.spawnOceans === "boolean"
              ? {
                  mode: rawGeneration.mode,
                  spawnBiome: rawGeneration.spawnBiome,
                  biomeSizeTenths: rawGeneration.biomeSizeTenths,
                  spawnStructures: rawGeneration.spawnStructures,
                  singleBiome: rawGeneration.singleBiome,
                  spawnCaves: rawGeneration.spawnCaves,
                  spawnOceans: rawGeneration.spawnOceans,
                }
              : current.generation;
            updateWorldCreation(store, {
              spawnBiomes: readSpawnBiomes(payload.spawnBiomes),
              visible: true,
              seed: payload.seed,
              ...(isDraft ? {
                name: typeof payload.name === "string" ? payload.name : current.name,
                mode: validMode(payload.mode) ? payload.mode : current.mode,
                ticksPerSecond: typeof payload.ticksPerSecond === "number"
                  ? String(payload.ticksPerSecond) : current.ticksPerSecond,
                spawnCreatures: typeof payload.spawnCreatures === "boolean"
                  ? payload.spawnCreatures : current.spawnCreatures,
                keepInventory: typeof payload.keepInventory === "boolean"
                  ? payload.keepInventory : current.keepInventory,
                creatureAttackMode: validAttackMode(payload.creatureAttackMode)
                  ? payload.creatureAttackMode : current.creatureAttackMode,
                generation,
              } : {}),
              pending: false,
              generating: false,
              errorKey: null,
            });
            if (isDraft) {
              store.update(state => ({
                ...state,
                navigation: { ...state.navigation, preWorldScreen: "new-world" },
                worldCatalog: { ...state.worldCatalog, status: "ready" },
              }));
            }
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
    case "newWorld.error.invalidCreatureAttackMode":
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
