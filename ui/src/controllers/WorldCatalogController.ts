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
      const catalog = store.getSnapshot().worldCatalog;
      if (catalog.status !== "ready" ||
          !catalog.worlds.some(world => world.id === id && world.compatible))
        return;
      // Lock locally before IPC: WebView click/keyboard activation can be
      // repeated before Godot acknowledges the first async load.
      store.update(state => ({
        ...state,
        worldCatalog: {
          ...state.worldCatalog, status: "verifying",
          loadError: false, deleteError: false,
        },
      }));
      postMessage("ui.world.catalog.load", { id });
    },
    recreateWorld(id: string) {
      const catalog = store.getSnapshot().worldCatalog;
      if (!id.trim() || catalog.status !== "ready" ||
          !catalog.worlds.some(world => world.id === id && world.compatible))
        return;
      store.update(state => ({
        ...state,
        worldCatalog: {
          ...state.worldCatalog, status: "verifying",
          loadError: false, deleteError: false,
        },
      }));
      postMessage("ui.world.catalog.recreate_draft", { id });
    },
    deleteWorld(id: string) {
      const catalog = store.getSnapshot().worldCatalog;
      if (catalog.status !== "ready" ||
          !catalog.worlds.some(world => world.id === id))
        return;
      // Prevent duplicate destructive requests before the native reply.
      store.update(state => ({
        ...state,
        worldCatalog: {
          ...state.worldCatalog, status: "deleting",
          deleteError: false, loadError: false,
        },
      }));
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
            worldCatalog: {
              ...state.worldCatalog,
              status: "ready", deleteError: true,
            },
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
          worldCatalog: {
            ...state.worldCatalog,
            status: "ready", loadError: true,
          },
        }));
        return;
      }

      if (message.type === "game.world_catalog.thumbnail") {
        const payload = asRecord(message.payload);
        const id = payload?.id;
        const url = payload?.thumbnailUrl;
        if (typeof id !== "string" || typeof url !== "string" ||
            url.length > 350000 ||
            !/^data:image\/png;base64,[A-Za-z0-9+/]+={0,2}$/.test(url)) return;
        const catalog = store.getSnapshot().worldCatalog;
        if (catalog.status !== "ready" ||
            !catalog.worlds.some(world => world.compatible && world.id === id)) return;
        store.update(state => ({
          ...state,
          worldCatalog: {
            ...state.worldCatalog,
            worlds: state.worldCatalog.worlds.map(world =>
              world.id === id && world.compatible
                ? { ...world, thumbnailUrl: url }
                : world),
          },
        }));
        return;
      }

      if (message.type !== "game.world_catalog") return;
      const payload = asRecord(message.payload);
      if (!payload) return;

      if (payload.status === "verifying") {
        store.update(state => ({
          ...state,
          worldCatalog: {
            ...state.worldCatalog,
            // A native failed load schedules a catalog rescan. Keep its
            // operation-specific error visible while that rescan runs.
            status: "verifying", folderError: false,
          },
        }));
        return;
      }

      if (payload.status === "error") {
        store.update(state => ({
          ...state,
          worldCatalog: {
            ...state.worldCatalog,
            status: "error", worlds: [], folderError: false,
            loadError: false,
          },
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
          loadError: state.worldCatalog.loadError,
        },
      }));
    },
  };
}

function readWorldSummary(value: unknown): WorldSummaryView | null {
  const entry = asRecord(value);
  if (!entry) return null;

  const lastSavedUnixMs = entry.lastSavedUnixMs;
  if (
      typeof entry.id !== "string" ||
      !entry.id.trim() ||
      !isLastSavedTimestamp(lastSavedUnixMs) ||
      typeof entry.seed !== "string" ||
      typeof entry.daysPassed !== "string" ||
      typeof entry.sphere !== "string" ||
      typeof entry.coordinates !== "string" ||
      typeof entry.compatible !== "boolean") return null;

  return {
    id: entry.id,
    lastSavedUnixMs,
    seed: entry.seed,
    daysPassed: entry.daysPassed,
    sphere: entry.sphere,
    coordinates: entry.coordinates,
    compatible: entry.compatible,
  };
}

function isLastSavedTimestamp(value: unknown): value is number | null {
  return value === null ||
    (typeof value === "number" &&
      Number.isSafeInteger(value) &&
      value > 0);
}
