import type { BridgeMessage } from "../bridge/godotBridge";
import type {
  GameplayInventoryState, InventoryCatalogEntry, InventorySlotState,
  InventoryEntryKind, InventoryMetadata,
} from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

function readKind(value: unknown): InventoryEntryKind | null {
  return value === "block" || value === "item" || value === "tool"
    ? value : null;
}

function readMetadata(value: unknown): InventoryMetadata | null {
  const raw = asRecord(value);
  if (!raw || Object.values(raw).some(x => typeof x !== "string"))
    return null;
  return raw as InventoryMetadata;
}

function readSlot(value: unknown): InventorySlotState {
  if (value === null) return null;
  const record = asRecord(value);
  if (!record || typeof record.id !== "string" ||
      !Number.isInteger(record.quantity) ||
      (record.quantity as number) < 1 ||
      (record.quantity as number) > 64) return null;
  const kind = readKind(record.kind);
  const metadata = readMetadata(record.metadata);
  if (!kind || !metadata ||
      (kind === "tool" && record.quantity !== 1)) return null;
  return {
    id: record.id, kind, quantity: record.quantity as number, metadata,
  };
}

function readSlots(value: unknown, count: number): InventorySlotState[] | null {
  if (!Array.isArray(value) || value.length !== count) return null;
  return value.map(readSlot);
}

export function createInventoryController(
  store: UiStore,
  post: (type: string, payload?: unknown) => void,
) {
  return {
    close() { post("ui.inventory.close"); },
    clickSlot(index: number) {
      if (!Number.isInteger(index) || index < 0 || index >= 36) return;
      post("ui.inventory.slot", { index });
    },
    sort() { post("ui.inventory.sort"); },
    discardCursor() { post("ui.inventory.discard_cursor"); },
    pickCreative(choice: InventoryCatalogEntry) {
      if (!choice.id) return;
      post("ui.inventory.creative_pick", {
        id: choice.id, kind: choice.kind,
        ...(Object.keys(choice.metadata).length > 0
          ? { metadata: choice.metadata } : {}),
      });
    },
    handleGodotMessage(message: BridgeMessage) {
      const payload = asRecord(message.payload);
      if (message.type === "game.inventory.state") {
        if (!payload || typeof payload.open !== "boolean" ||
            typeof payload.creativeAvailable !== "boolean" ||
            !Number.isInteger(payload.selectedIndex) ||
            (payload.selectedIndex as number) < 0 ||
            (payload.selectedIndex as number) >= 9)
          return;
        const backpack = readSlots(payload.backpack, 27);
        const hotbar = readSlots(payload.hotbar, 9);
        if (!backpack || !hotbar) return;
        const open = payload.open;
        store.update(state => {
          const inventory: GameplayInventoryState = {
            ...state.inventory,
            open,
            creativeAvailable: payload.creativeAvailable as boolean,
            selectedIndex: payload.selectedIndex as number,
            backpack, hotbar, cursor: readSlot(payload.cursor),
            errorKey: null,
          };
          return {
            ...state,
            inventory,
            navigation: {
              ...state.navigation,
              overlay: open ? "inventory"
                : state.navigation.overlay === "inventory" ? "none"
                : state.navigation.overlay,
            },
          };
        });
      } else if (message.type === "game.inventory.catalog") {
        if (!payload || !Array.isArray(payload.items)) return;
        const catalog = payload.items.flatMap((raw): InventoryCatalogEntry[] => {
          const item = asRecord(raw);
          const kind = readKind(item?.kind);
          const metadata = readMetadata(item?.metadata);
          return item && kind && metadata &&
            typeof item.id === "string" &&
            typeof item.name === "string" &&
            typeof item.category === "string"
              ? [{
                id: item.id, kind, name: item.name,
                category: item.category, metadata,
              }]
              : [];
        });
        store.update(state => ({
          ...state,
          inventory: { ...state.inventory, catalog },
        }));
      } else if (message.type === "game.inventory.error") {
        const errorKey = payload?.code === "InventoryFull"
          ? "inventory.error.full"
          : payload?.code === "UnsupportedDropKind"
            ? "inventory.error.unsupportedDrop"
            : "inventory.error.invalid";
        store.update(state => ({
          ...state,
          inventory: { ...state.inventory, errorKey },
        }));
      }
    },
  };
}
