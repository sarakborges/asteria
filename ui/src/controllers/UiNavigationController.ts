import type { OverlayScreen } from "../state/uiState";
import type { BridgeMessage } from "../bridge/godotBridge";
import type { UiStore } from "../state/uiStore";

export type UiNavigationController = {
  openWorldSelection(): void;
  openWorldCreation(): void;
  backToStart(): void;
  exitGame(): void;
  resumeGame(): void;
  openGameSettings(): void;
  openWorldSettings(): void;
  openControls(): void;
  backFromOverlay(): void;
  handleGodotMessage(message: BridgeMessage): void;
};

export function createUiNavigationController(
  store: UiStore,
  postMessage: (type: string, payload?: unknown) => void,
): UiNavigationController {
  const setPreWorldScreen = (
    preWorldScreen: "starting" | "world-selection" | "new-world",
  ): void => {
    store.update((state) => ({
      ...state,
      navigation: {
        ...state.navigation,
        preWorldScreen,
      },
    }));
  };

  const show = (overlay: OverlayScreen) =>
    store.update(state => ({
      ...state, navigation: { ...state.navigation, overlay },
    }));

  return {
    openWorldSelection() {
      setPreWorldScreen("world-selection");
      postMessage("ui.world.catalog.refresh");
    },

    openWorldCreation() {
      setPreWorldScreen("new-world");
    },

    backToStart() {
      setPreWorldScreen("starting");
    },

    exitGame() { postMessage("ui.app.exit"); },
    resumeGame() { postMessage("ui.game.resume"); },
    openGameSettings() { show("game"); },
    openWorldSettings() { show("world"); },
    openControls() { show("controls"); },
    backFromOverlay() {
      show(store.getSnapshot().worldCreation.visible ? "none" : "pause");
    },
    handleGodotMessage(message) {
      if (message.type !== "game.mouse_capture") return;
      const payload = message.payload as { captured?: unknown } | undefined;
      if (payload?.captured === true) {
        show("none");
      } else {
        const state = store.getSnapshot();
        if (state.mouseCaptured && !state.worldCreation.visible &&
            !state.loading && state.navigation.overlay === "none") show("pause");
      }
    },
  };
}
