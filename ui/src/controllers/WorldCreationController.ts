import type { BridgeMessage } from "../bridge/godotBridge";
import type { NewWorldPageView } from "../components/pages/NewWorldPage";

export type WorldCreationController = {
  mount(): void;
  handleGodotMessage(message: BridgeMessage): void;
};

export function createWorldCreationController(
  view: NewWorldPageView,
  postMessage: (type: string, payload?: unknown) => void,
): WorldCreationController {
  return {
    mount() {
      view.form.addEventListener("submit", (event) => {
        event.preventDefault();
        view.setPending(true);
        postMessage("ui.world.create", {
          seed: view.seedInput.value.trim(),
        });
      });

      view.randomizeButton.addEventListener("click", () => {
        view.setPending(true);
        postMessage("ui.world.randomize");
      });
    },

    handleGodotMessage(message) {
      switch (message.type) {
        case "game.world_creation": {
          const payload = asRecord(message.payload);
          if (payload && typeof payload.seed === "string") {
            view.setSuggestedSeed(payload.seed);
          }
          break;
        }

        case "game.world_creation.error": {
          const payload = asRecord(message.payload);
          view.setError(
            payload && typeof payload.message === "string"
              ? payload.message
              : "Não foi possível criar o mundo.",
          );
          break;
        }

        case "game.world_creation.started": {
          const payload = asRecord(message.payload);
          if (payload && typeof payload.seed === "string") {
            view.setGenerating(payload.seed);
          }
          break;
        }

        case "game.chunk_ready":
          view.hide();
          break;
      }
    },
  };
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value !== null && typeof value === "object" && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null;
}
