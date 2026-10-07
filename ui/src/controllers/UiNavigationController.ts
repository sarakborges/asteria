import type { UiStore } from "../state/uiStore";

export type UiNavigationController = {
  openWorldCreation(): void;
  backToStart(): void;
  exitGame(): void;
};

export function createUiNavigationController(
  store: UiStore,
  postMessage: (type: string, payload?: unknown) => void,
): UiNavigationController {
  const setPreWorldScreen = (
    preWorldScreen: "starting" | "new-world",
  ): void => {
    store.update((state) => ({
      ...state,
      navigation: {
        ...state.navigation,
        preWorldScreen,
      },
    }));
  };

  return {
    openWorldCreation() {
      setPreWorldScreen("new-world");
    },

    backToStart() {
      setPreWorldScreen("starting");
    },

    exitGame() {
      postMessage("ui.app.exit");
    },
  };
}
