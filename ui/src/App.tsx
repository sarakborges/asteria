import { useEffect } from "react";
import { LoadingOverlay } from "./components/organisms/LoadingOverlay/LoadingOverlay";
import { GameHudPage } from "./components/pages/GameHudPage/GameHudPage";
import { NewWorldPage } from "./components/pages/NewWorldPage/NewWorldPage";
import type { UiStore } from "./state/uiStore";
import { useUiStore } from "./state/useUiStore";

export type AppActions = {
  ping(): void;
  createWorld(seed: string): void;
  randomizeWorld(): void;
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

  return (
    <>
      <GameHudPage
        embedded={embedded}
        state={state.hud}
        onPing={actions.ping}
        onDismissToast={actions.dismissToast}
      />
      <NewWorldPage
        state={state.worldCreation}
        onCreate={actions.createWorld}
        onRandomize={actions.randomizeWorld}
      />
      <LoadingOverlay state={state.loading} />
    </>
  );
}
