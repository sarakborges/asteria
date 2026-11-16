import type { BridgeMessage } from "../bridge/godotBridge";
import type { BrushPaletteColor } from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

const rgbPattern = /^#[0-9a-fA-F]{6}$/;

export function createBrushController(
  store: UiStore,
  post: (type: string, payload?: unknown) => void,
) {
  return {
    close() { post("ui.brush.close"); },
    select(id: string | null) {
      if (id !== null &&
          !store.getSnapshot().brush.colors.some(color => color.id === id))
        return;
      post("ui.brush.select", { id });
    },
    handleGodotMessage(message: BridgeMessage) {
      if (message.type !== "game.tool.brush_palette") return;
      const payload = asRecord(message.payload);
      if (!payload || typeof payload.open !== "boolean" ||
          (payload.selectedId !== null &&
           typeof payload.selectedId !== "string") ||
          !Array.isArray(payload.colors)) return;
      const colors = payload.colors.flatMap((value): BrushPaletteColor[] => {
        const color = asRecord(value);
        return color && typeof color.id === "string" &&
          /^[-a-z0-9_]+:[-a-z0-9_\/]+$/.test(color.id) &&
          typeof color.rgb === "string" && rgbPattern.test(color.rgb)
          ? [{ id: color.id, rgb: color.rgb }] : [];
      });
      if (colors.length !== payload.colors.length ||
          new Set(colors.map(color => color.id)).size !== colors.length)
        return;
      store.update(state => ({
        ...state,
        brush: {
          selectedId: payload.selectedId as string | null,
          colors,
        },
        navigation: {
          ...state.navigation,
          overlay: payload.open ? "brush" :
            state.navigation.overlay === "brush" ? "none" :
            state.navigation.overlay,
        },
      }));
    },
  };
}
