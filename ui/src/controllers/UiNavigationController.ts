import { createInitialUiState, type OverlayScreen } from "../state/uiState";
import type { BridgeMessage } from "../bridge/godotBridge";
import type { UiStore } from "../state/uiStore";

export type UiNavigationController = {
  openWorldSelection(): void;
  openWorldCreation(): void;
  backToStart(): void;
  exitGame(): void;
  resumeGame(): void;
  saveWorld(): void;
  leaveWorld(): void;
  respawn(): void;
  openGameSettings(): void;
  openWorldSettings(): void;
  openControls(): void;
  backFromOverlay(): void;
  escape(): void;
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

  // Only explicit WebUI navigation is handled here. Gameplay hotkeys and
  // mouse capture remain native; a paused menu never directly captures it.
  const escape = () => {
    const state = store.getSnapshot();
    if (state.navigation.death) return;
    if (state.navigation.overlay === "storage") {
      postMessage("ui.storage_box.close");
    } else if (state.navigation.overlay === "game" ||
        state.navigation.overlay === "world" ||
        state.navigation.overlay === "controls") {
      show(state.worldCreation.visible ? "none" : "pause");
    } else if (state.navigation.overlay === "pause") {
      postMessage("ui.game.resume");
    } else if (state.worldCreation.visible) {
      if (state.navigation.preWorldScreen === "new-world")
        setPreWorldScreen("world-selection");
      else if (state.navigation.preWorldScreen === "world-selection")
        setPreWorldScreen("starting");
    }
  };

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
    saveWorld() {
      store.update(state => ({
        ...state,
        navigation: { ...state.navigation, saveFeedback: null },
      }));
      postMessage("ui.world.save");
    },
    respawn() {
      if (store.getSnapshot().navigation.death)
        postMessage("ui.player.respawn");
    },
    leaveWorld() {
      store.update(state => ({
        ...state,
        navigation: { ...state.navigation, saveFeedback: null },
      }));
      postMessage("ui.world.leave");
    },
    openGameSettings() { show("game"); },
    openWorldSettings() { show("world"); },
    openControls() { show("controls"); },
    backFromOverlay() {
      show(store.getSnapshot().worldCreation.visible ? "none" : "pause");
    },
    escape,
    handleGodotMessage(message) {
      if (message.type === "game.world.left") {
        store.update(previous => {
          const initial = createInitialUiState(true);
          return {
            ...initial,
            navigation: { ...initial.navigation, preWorldScreen: "world-selection" },
            settings: { ...initial.settings, client: previous.settings.client },
            worldCatalog: { ...previous.worldCatalog, status: "verifying", deleteError: false },
            worldCreation: {
              ...initial.worldCreation,
              spawnBiomes: previous.worldCreation.spawnBiomes,
            },
          };
        });
        return;
      }
      if (message.type === "game.player.death") {
        const raw = message.payload as Record<string, unknown> | null;
        if (!raw || typeof raw.keepInventory !== "boolean" ||
            typeof raw.droppedStacks !== "number" ||
            !Number.isSafeInteger(raw.droppedStacks) ||
            raw.droppedStacks < 0 || raw.droppedStacks > 42 ||
            typeof raw.dropCapacityExceeded !== "boolean" ||
            (raw.outcomeKnown !== undefined && typeof raw.outcomeKnown !== "boolean"))
          return;
        store.update(state => ({
          ...state,
          navigation: {
            ...state.navigation,
            overlay: "death",
            death: {
              keepInventory: raw.keepInventory as boolean,
              droppedStacks: raw.droppedStacks as number,
              dropCapacityExceeded: raw.dropCapacityExceeded as boolean,
              outcomeKnown: raw.outcomeKnown !== false,
            },
          },
        }));
        return;
      }
      if (message.type === "game.player.respawned") {
        store.update(state => ({
          ...state,
          navigation: { ...state.navigation, overlay: "none", death: null },
        }));
        return;
      }
      if (message.type === "game.world.save_result") {
        const payload = message.payload as { status?: unknown } | undefined;
        const saveFeedback = payload?.status === "saved"
          ? "saved"
          : payload?.status === "error" ? "error" : null;
        if (saveFeedback) store.update(state => ({
          ...state,
          navigation: { ...state.navigation, saveFeedback },
        }));
        return;
      }
      if (message.type === "game.ui.escape") {
        escape();
        return;
      }
      if (message.type !== "game.mouse_capture") return;
      const payload = message.payload as { captured?: unknown } | undefined;
      if (store.getSnapshot().navigation.death) return;
      if (payload?.captured === true) {
        show("none");
      } else {
        const state = store.getSnapshot();
        if (state.mouseCaptured && !state.chat.open && !state.worldCreation.visible &&
            !state.loading && state.navigation.overlay === "none" &&
            !state.storageBox.open) show("pause");
      }
    },
  };
}
