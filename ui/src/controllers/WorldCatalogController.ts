import type { BridgeMessage } from "../bridge/godotBridge";
import type { WorldSummaryView } from "../presentation/worldCatalogModels";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

const MAX_WORLDS = 256;

export function createWorldCatalogController(
  store: UiStore,
  postMessage: (type: string, payload?: unknown) => void,
) {
  return {
    openSavesFolder() {
      postMessage("ui.world.catalog.open_folder");
    },
    loadWorld(id: string) {
      if (id.trim() && store.getSnapshot().worldCatalog.status === "ready")
        postMessage("ui.world.catalog.load", { id });
    },
    deleteWorld(id: string) {
      const state = store.getSnapshot().worldCatalog;
      if (id.trim() && state.status === "ready" &&
          state.worlds.some(world => world.id === id))
        postMessage("ui.world.catalog.delete", { id });
    },
    handleGodotMessage(message: BridgeMessage) {
      if (message.type === "game.world_catalog.delete_result") {
        const payload = asRecord(message.payload);
        if (payload?.status === "deleting") {
          store.update(state => ({
            ...state,
            worldCatalog: { ...state.worldCatalog, status: "deleting", deleteError: false },
          }));
        } else if (payload?.status === "error") {
          store.update(state => ({
            ...state,
            worldCatalog: { ...state.worldCatalog, deleteError: true },
          }));
        }
        return;
      }

      if (message.type === "game.world_catalog.folder_error") {
        store.update(state => ({
          ...state,
          worldCatalog: { ...state.worldCatalog, folderError: true },
        }));
        return;
      }

      if (message.type === "game.world_catalog.load_error") {
        store.update(state => ({
          ...state,
          worldCatalog: { ...state.worldCatalog, status: "error" },
        }));
        return;
      }

      if (message.type !== "game.world_catalog") return;
      const payload = asRecord(message.payload);
      if (!payload) return;

      if (payload.status === "verifying") {
        store.update(state => ({
          ...state,
          worldCatalog: { ...state.worldCatalog, status: "verifying", folderError: false },
        }));
        return;
      }

      if (payload.status === "error") {
        store.update(state => ({
          ...state,
          worldCatalog: { ...state.worldCatalog, status: "error", worlds: [], folderError: false },
        }));
        return;
      }

      if (payload.status !== "ready" || !Array.isArray(payload.worlds)) return;
      const worlds = payload.worlds.map(readWorldSummary);
      if (worlds.length > MAX_WORLDS || worlds.some(world => world === null)) {
        store.update(state => ({
          ...state,
          worldCatalog: { ...state.worldCatalog, status: "error", worlds: [], folderError: false },
        }));
        return;
      }
      store.update(state => ({
        ...state,
        worldCatalog: {
          status: "ready",
          worlds: worlds as WorldSummaryView[],
          folderError: false,
          deleteError: state.worldCatalog.deleteError,
        },
      }));
    },
  };
}

function readWorldSummary(value: unknown): WorldSummaryView | null {
  const entry = asRecord(value);
  if (!entry ||
      typeof entry.id !== "string" ||
      !entry.id.trim() ||
      typeof entry.lastSaved !== "string" ||
      typeof entry.seed !== "string" ||
      typeof entry.daysPassed !== "string" ||
      typeof entry.sphere !== "string" ||
      typeof entry.coordinates !== "string" ||
      typeof entry.compatible !== "boolean") return null;

  return {
    id: entry.id,
    lastSaved: entry.lastSaved,
    seed: entry.seed,
    daysPassed: entry.daysPassed,
    sphere: entry.sphere,
    coordinates: entry.coordinates,
    compatible: entry.compatible,
  };
}
