import type { BridgeMessage } from "../bridge/godotBridge";
import type { LoadingOverlayView } from "../components/organisms/LoadingOverlay";

export type LoadingController = {
  handleGodotMessage(message: BridgeMessage): void;
};

export function createLoadingController(
  view: LoadingOverlayView,
): LoadingController {
  return {
    handleGodotMessage(message) {
      if (message.type === "game.chunk_ready") {
        view.hide();
        return;
      }

      if (message.type !== "game.loading") {
        return;
      }

      const payload = asRecord(message.payload);
      if (!payload) {
        return;
      }

      const phase =
        typeof payload.phase === "string"
          ? payload.phase
          : "";
      if (phase === "ready") {
        view.hide();
        return;
      }

      const completed =
        typeof payload.completed === "number"
          ? Math.max(0, Math.trunc(payload.completed))
          : 0;
      const total =
        typeof payload.total === "number"
          ? Math.max(0, Math.trunc(payload.total))
          : 0;
      const dimension =
        typeof payload.dimension === "string"
          ? payload.dimension
          : "";

      view.show({
        phaseLabel: phaseLabel(phase),
        completed,
        total,
        dimension,
      });
    },
  };
}

function phaseLabel(phase: string): string {
  switch (phase) {
    case "retiring_current_dimension":
      return "Encerrando Sphere atual";
    case "materializing_initial_area":
      return "Materializando área inicial";
    case "preparing_presentation":
      return "Preparando apresentação";
    default:
      return "Preparando mundo";
  }
}

function asRecord(
  value: unknown,
): Record<string, unknown> | null {
  return value !== null &&
    typeof value === "object" &&
    !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}
