import type { BridgeMessage } from "../bridge/godotBridge";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

export type SavingController = {
  handleGodotMessage(message: BridgeMessage): void;
};

export function createSavingController(store: UiStore): SavingController {
  return {
    handleGodotMessage(message) {
      if (message.type !== "game.world.saving") return;

      const payload = asRecord(message.payload);
      if (!payload || typeof payload.active !== "boolean") return;

      store.update(state => ({
        ...state,
        saving: payload.active as boolean,
        loading: payload.active ? null : state.loading,
      }));
    },
  };
}
