import type { BridgeMessage } from "../bridge/godotBridge";
import type { LoadingPhase, LoadingState } from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";

export type LoadingController = {
  handleGodotMessage(message: BridgeMessage): void;
};

export function createLoadingController(
  store: UiStore,
): LoadingController {
  return {
    handleGodotMessage(message) {
      if (message.type === "game.chunk_ready") {
        setLoading(store, null);
        return;
      }

      if (message.type !== "game.loading") {
        return;
      }

      const payload = asRecord(message.payload);
      if (!payload) return;

      const phase =
        typeof payload.phase === "string"
          ? payload.phase
          : "";

      if (phase === "ready") {
        setLoading(store, null);
        return;
      }

      setLoading(store, {
        phase: readLoadingPhase(phase),
        completed:
          typeof payload.completed === "number"
            ? Math.max(0, Math.trunc(payload.completed))
            : 0,
        total:
          typeof payload.total === "number"
            ? Math.max(0, Math.trunc(payload.total))
            : 0,
        dimension:
          typeof payload.dimension === "string"
            ? payload.dimension
            : "",
      });
    },
  };
}

function setLoading(
  store: UiStore,
  loading: LoadingState | null,
): void {
  store.update((state) => ({
    ...state,
    loading,
  }));
}

function readLoadingPhase(phase: string): LoadingPhase {
  switch (phase) {
    case "retiring_current_dimension":
    case "materializing_initial_area":
    case "preparing_presentation":
      return phase;
    default:
      return "preparing_world";
  }
}
