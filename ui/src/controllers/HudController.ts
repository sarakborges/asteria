import type { BridgeMessage } from "../bridge/godotBridge";
import type { GameHudPageView } from "../components/pages/GameHudPage";

export type HudController = {
  mount(): void;
  handleGodotMessage(message: BridgeMessage): void;
};

export function createHudController(
  view: GameHudPageView,
  postMessage: (type: string, payload?: unknown) => void,
): HudController {
  const handleContextMenu = (event: Event): void => {
    event.preventDefault();
  };

  const handlePing = (): void => {
    view.statusCard.lastMessage.textContent =
      "webui → godot: ping sent";
    postMessage("ui.ping");
  };

  return {
    mount() {
      document.addEventListener("contextmenu", handleContextMenu);
      view.statusCard.pingButton.addEventListener("click", handlePing);
    },

    handleGodotMessage(message) {
      view.statusCard.lastMessage.textContent =
        `godot → webui: ${message.type}`;

      switch (message.type) {
        case "game.ready":
          view.statusCard.bridgeStatus.setLabel("bridge connected");
          view.statusCard.bridgeStatus.setTone("connected");
          break;

        case "game.chunk_ready":
          view.statusCard.worldStatus.textContent =
            "chunk generated + collision ready";
          break;

        case "game.player_ready":
          view.statusCard.playerStatus.textContent =
            "FPS controller ready";
          break;

        case "game.mouse_capture": {
          const payload = message.payload as
            | { captured?: boolean }
            | undefined;

          document.documentElement.classList.toggle(
            "mouse-captured",
            payload?.captured === true,
          );
          break;
        }

        case "game.pong":
          view.statusCard.lastMessage.textContent =
            "godot → webui: pong received";
          break;
      }
    },
  };
}
