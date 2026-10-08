import type { BridgeMessage } from "../bridge/godotBridge";
import type {
  GameplayInventoryState, InventoryCatalogEntry,
} from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";
import { readKind, readMetadata, readSlot, readSlots } from "./inventoryMessageSlots";

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
                iconUrl: typeof item.iconUrl === "string" &&
                  item.iconUrl.startsWith("data:image/png;base64,")
                    ? item.iconUrl : undefined,
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
          : "inventory.error.invalid";
        store.update(state => ({
          ...state,
          inventory: { ...state.inventory, errorKey },
        }));
      }
    },
  };
}
