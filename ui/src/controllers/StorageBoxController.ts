import type { BridgeMessage } from "../bridge/godotBridge";
import type { InventorySlotState, StorageBoxState } from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";
import { readSlots } from "./inventoryMessageSlots";

function readPosition(value: unknown): StorageBoxState["position"] {
  const record = asRecord(value);
  if (!record || ![record.x, record.y, record.z].every(Number.isSafeInteger))
    return null;
  if ((record.y as number) < 0) return null;
  return { x: record.x as number, y: record.y as number, z: record.z as number };
}

export function createStorageBoxController(
  store: UiStore,
  post: (type: string, payload?: unknown) => void,
) {
  return {
    close() { post("ui.storage_box.close"); },
    clickSlot(index: number) {
      if (!Number.isInteger(index) || index < 0 || index >= 27) return;
      post("ui.storage_box.slot", { index });
    },
    sort() { post("ui.storage_box.sort"); },
    handleGodotMessage(message: BridgeMessage) {
      const payload = asRecord(message.payload);
      if (message.type === "game.storage_box.state") {
        if (!payload || typeof payload.open !== "boolean") return;
        const open = payload.open;
        const position = open ? readPosition(payload.position) : null;
        const slots: InventorySlotState[] | null = open
          ? readSlots(payload.slots, 27) : Array(27).fill(null);
        if (open && (!position || !slots)) return;
        store.update(state => ({
          ...state,
          storageBox: { open, position, slots: slots!, errorKey: null },
          navigation: {
            ...state.navigation,
            overlay: open ? "storage" :
              state.navigation.overlay === "storage" ? "none" : state.navigation.overlay,
          },
        }));
      } else if (message.type === "game.storage_box.error") {
        const errorKey = payload?.code === "InventoryFull"
          ? "inventory.error.full" : "inventory.error.invalid";
        store.update(state => ({
          ...state,
          storageBox: { ...state.storageBox, errorKey },
        }));
      }
    },
  };
}
