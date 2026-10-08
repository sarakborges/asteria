import { useEffect } from "react";
import { LoadingOverlay } from "./components/organisms/LoadingOverlay/LoadingOverlay";
import { GameHudPage } from "./components/pages/GameHudPage/GameHudPage";
import { SettingsWorkspacePage } from "./components/pages/SettingsWorkspacePage/SettingsWorkspacePage";
import { ControlsPage } from "./components/pages/ControlsPage/ControlsPage";
import { PauseMenuPage } from "./components/pages/PauseMenuPage/PauseMenuPage";
import { useLocalization } from "./localization/LocalizationProvider";
import { NewWorldPage } from "./components/pages/NewWorldPage/NewWorldPage";
import { StartingScreenPage } from "./components/pages/StartingScreenPage/StartingScreenPage";
import type { UiStore } from "./state/uiStore";
import { useUiStore } from "./state/useUiStore";

export type AppActions = {
  ping(): void;
  createWorld(request: {
    seed: string; name: string;
    mode: import("./state/uiState").GameMode;
    ticksPerSecond: string;
  }): void;
  randomizeWorld(): void;
  openWorldCreation(): void;
  backToStart(): void;
  exitGame(): void;
  dismissToast(id: number): void;
  resumeGame(): void;
  openGameSettings(): void;
  openWorldSettings(): void;
  openControls(): void;
  backFromOverlay(): void;
  setRenderDistance(value: number): void;
  setTargetPosition(value: "Center" | "TopRight" | "Hidden"): void;
  setWorldTicks(value: number): void;
  setGameMode(value: import("./state/uiState").GameMode): void;
  beginKeyCapture(action: "Jump" | "Descend"): void;
  cancelKeyCapture(): void;
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
  const { t } = useLocalization();

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
            settingsAvailable={Boolean(state.settings.client)}
            controlsAvailable={Boolean(state.settings.client)}
            onSettings={actions.openGameSettings}
            onControls={actions.openControls}
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

      {!preWorldVisible &&
        state.navigation.overlay === "pause" && (
          <PauseMenuPage
            worldSettingsAvailable={Boolean(state.settings.world)}
            gameSettingsAvailable={Boolean(state.settings.client)}
            controlsAvailable={Boolean(state.settings.client)}
            onResume={actions.resumeGame}
            onWorldSettings={actions.openWorldSettings}
            onGameSettings={actions.openGameSettings}
            onControls={actions.openControls}
            onExitGame={actions.exitGame}
          />
        )}

      {(state.navigation.overlay === "game" ||
        state.navigation.overlay === "world") && (
        <SettingsWorkspacePage
          scope={state.navigation.overlay}
          settings={state.settings}
          onBack={actions.backFromOverlay}
          onRenderDistance={actions.setRenderDistance}
          onTargetPosition={actions.setTargetPosition}
          onWorldTicks={actions.setWorldTicks}
          onGameMode={actions.setGameMode}
        />
      )}

      {state.navigation.overlay === "controls" && (
        <div className="settings-workspace">
          <ControlsPage
            groups={[{
              title: t("settings.section.keybinds"),
              entries: [
                { key: state.settings.client?.keybinds.jump ?? "Space",
                  action: t("settings.keybind.jump"), bindAction: "Jump" },
                { key: state.settings.client?.keybinds.descend ?? "ShiftLeft",
                  action: t("settings.keybind.descend"), bindAction: "Descend" },
                { key: "WASD", action: t("settings.controls.movement") },
                { key: "ESC", action: t("settings.controls.pause") },
              ],
            }]}
            capturingAction={state.settings.captureAction}
            captureError={state.settings.errorKey ? t(state.settings.errorKey) : null}
            onCapture={actions.beginKeyCapture}
            onCancelCapture={actions.cancelKeyCapture}
            onBack={() => {
              if (state.settings.captureAction) actions.cancelKeyCapture();
              actions.backFromOverlay();
            }}
          />
        </div>
      )}

      <LoadingOverlay state={state.loading} />
    </>
  );
}
