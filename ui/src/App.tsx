import { useEffect } from "react";
import type { InventoryCatalogEntry } from "./state/uiState";
import { LoadingOverlay } from "./components/organisms/LoadingOverlay/LoadingOverlay";
import { GameHudPage } from "./components/pages/GameHudPage/GameHudPage";
import { InventoryGameplayPage } from "./components/pages/InventoryGameplayPage/InventoryGameplayPage";
import { SettingsWorkspacePage } from "./components/pages/SettingsWorkspacePage/SettingsWorkspacePage";
import { ControlsPage } from "./components/pages/ControlsPage/ControlsPage";
import { PauseMenuPage } from "./components/pages/PauseMenuPage/PauseMenuPage";
import { useLocalization } from "./localization/LocalizationProvider";
import { BrushPalettePage } from "./components/pages/BrushPalettePage/BrushPalettePage";
import { NewWorldPage } from "./components/pages/NewWorldPage/NewWorldPage";
import { WorldSelectionPage } from "./components/pages/WorldSelectionPage/WorldSelectionPage";
import { StartingScreenPage } from "./components/pages/StartingScreenPage/StartingScreenPage";
import type { UiStore } from "./state/uiStore";
import { useUiStore } from "./state/useUiStore";
import { buildControlGroups } from "./presentation/controlGroups";

export type AppActions = {
  ping(): void;
  createWorld(request: {
    seed: string; name: string;
    mode: import("./state/uiState").GameMode;
    ticksPerSecond: string;
  }): void;
  randomizeWorld(): void;
  openWorldSelection(): void;
  openSavesFolder(): void;
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
  setHideHints(value: boolean): void;
  setGameplayHint(kind: "RotateBlock" | "BreakOrPlaceBlock", value: boolean): void;
  setWorldTicks(value: number): void;
  setGameMode(value: import("./state/uiState").GameMode): void;
  beginKeyCapture(action: "Jump" | "Descend" | "ToolAction" | "Inventory" | "DropItem"): void;
  cancelKeyCapture(): void;
  closeBrushPalette(): void;
  selectBrushDye(id: string | null): void;
  closeInventory(): void;
  clickInventorySlot(index: number): void;
  sortInventory(): void;
  discardInventoryCursor(): void;
  pickCreativeBlock(choice: InventoryCatalogEntry): void;
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
        catalog={state.inventory.catalog}
        onPing={actions.ping}
        onDismissToast={actions.dismissToast}
      />

      {preWorldVisible &&
        state.navigation.preWorldScreen ===
          "starting" && (
          <StartingScreenPage
            onPlay={actions.openWorldSelection}
            onExit={actions.exitGame}
            settingsAvailable={Boolean(state.settings.client)}
            controlsAvailable={Boolean(state.settings.client)}
            onSettings={actions.openGameSettings}
            onControls={actions.openControls}
          />
        )}

      {preWorldVisible &&
        state.navigation.preWorldScreen ===
          "world-selection" && (
          <WorldSelectionPage
            worlds={state.worldCatalog.worlds}
            status={
              state.worldCatalog.status === "verifying"
                ? t("worldSelection.verifying")
                : state.worldCatalog.status === "unavailable"
                  ? t("worldSelection.unavailable")
                  : state.worldCatalog.status === "ready" &&
                      state.worldCatalog.worlds.length === 0
                    ? t("worldSelection.noRestorable")
                    : undefined
            }
            error={
              state.worldCatalog.status === "error"
                ? t("worldSelection.scanError")
                : state.worldCatalog.folderError
                  ? t("worldSelection.openSavesFolderError")
                  : undefined
            }
            onBack={actions.backToStart}
            onCreateWorld={actions.openWorldCreation}
            onOpenSavesFolder={actions.openSavesFolder}
          />
        )}

      {preWorldVisible &&
        state.navigation.preWorldScreen ===
          "new-world" && (
          <NewWorldPage
            state={state.worldCreation}
            onBack={actions.openWorldSelection}
            onMainMenu={actions.backToStart}
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
          onHideHints={actions.setHideHints}
          onGameplayHint={actions.setGameplayHint}
          onWorldTicks={actions.setWorldTicks}
          onGameMode={actions.setGameMode}
        />
      )}

      {state.navigation.overlay === "controls" && (
        <div className="settings-workspace">
          <ControlsPage
            groups={buildControlGroups(t, state.settings.client?.keybinds)}
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

      {!preWorldVisible && state.navigation.overlay === "brush" && (
        <BrushPalettePage
          state={state.brush}
          onClose={actions.closeBrushPalette}
          onSelect={actions.selectBrushDye}
        />
      )}

      {!preWorldVisible && state.navigation.overlay === "inventory" && (
        <InventoryGameplayPage
          state={state.inventory}
          onClose={actions.closeInventory}
          onSlotClick={actions.clickInventorySlot}
          onSort={actions.sortInventory}
          onDiscardCursor={actions.discardInventoryCursor}
          onCreativePick={actions.pickCreativeBlock}
        />
      )}

      <LoadingOverlay state={state.loading} />
    </>
  );
}
