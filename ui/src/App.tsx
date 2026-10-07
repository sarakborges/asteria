import { useEffect } from "react";
import { LoadingOverlay } from "./components/organisms/LoadingOverlay/LoadingOverlay";
import { GameHudPage } from "./components/pages/GameHudPage/GameHudPage";
import { NewWorldPage } from "./components/pages/NewWorldPage/NewWorldPage";
import { StartingScreenPage } from "./components/pages/StartingScreenPage/StartingScreenPage";
import type { UiStore } from "./state/uiStore";
import { useUiStore } from "./state/useUiStore";

export type AppActions = {
  ping(): void;
  createWorld(seed: string): void;
  randomizeWorld(): void;
  openWorldCreation(): void;
  backToStart(): void;
  exitGame(): void;
  dismissToast(id: number): void;
};

export type AppProps = {
  embedded: boolean;
  store: UiStore;
  actions: AppActions;
};

export function App({
  embedded,
  store,
  actions,
}: AppProps) {
  const state = useUiStore(store);

  useEffect(() => {
    document.documentElement.classList.toggle(
      "mouse-captured",
      state.mouseCaptured,
    );
  }, [state.mouseCaptured]);

  const preWorldVisible =
    state.worldCreation.visible;

  return (
    <>
      <GameHudPage
        embedded={embedded}
        state={state.hud}
        onPing={actions.ping}
        onDismissToast={actions.dismissToast}
      />

      {preWorldVisible &&
        state.navigation.preWorldScreen ===
          "starting" && (
          <StartingScreenPage
            onPlay={actions.openWorldCreation}
            onExit={actions.exitGame}
          />
        )}

      {preWorldVisible &&
        state.navigation.preWorldScreen ===
          "new-world" && (
          <NewWorldPage
            state={state.worldCreation}
            onBack={actions.backToStart}
            onCreate={actions.createWorld}
            onRandomize={actions.randomizeWorld}
          />
        )}

      <LoadingOverlay state={state.loading} />
    </>
  );
}
