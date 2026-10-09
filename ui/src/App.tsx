import { useEffect, useRef, type KeyboardEvent } from "react";
import type { InventoryCatalogEntry } from "./state/uiState";
import { LoadingOverlay } from "./components/organisms/LoadingOverlay/LoadingOverlay";
import { GameHudPage } from "./components/pages/GameHudPage/GameHudPage";
import { ChatDock } from "./components/organisms/ChatDock/ChatDock";
import { InventoryGameplayPage } from "./components/pages/InventoryGameplayPage/InventoryGameplayPage";
import { StorageBoxPage } from "./components/pages/StorageBoxPage/StorageBoxPage";
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
import { ScreenTransition } from "./components/templates/ScreenTransition/ScreenTransition";

export type AppActions = {
  ping(): void;
  createWorld(request: {
    seed: string; name: string;
    mode: import("./state/uiState").GameMode;
    ticksPerSecond: string;
    spawnCreatures: boolean;
    generation: import("./state/uiState").WorldGenerationDraft;
  }): void;
  randomizeWorld(): void;
  openWorldSelection(): void;
  openSavesFolder(): void;
  loadWorld(id: string): void;
  openWorldCreation(): void;
  backToStart(): void;
  exitGame(): void;
  dismissToast(id: number): void;
  resumeGame(): void;
  saveWorld(): void;
  openGameSettings(): void;
  openWorldSettings(): void;
  openControls(): void;
  backFromOverlay(): void;
  escapeNavigation(): void;
  setRenderDistance(value: number): void;
  setTargetPosition(value: "Center" | "TopRight" | "Hidden"): void;
  setHideHints(value: boolean): void;
  setGameplayHint(kind: "RotateBlock" | "BreakOrPlaceBlock", value: boolean): void;
  setWorldTicks(value: number): void;
  setSpawnCreatures(value: boolean): void;
  setGameMode(value: import("./state/uiState").GameMode): void;
  beginKeyCapture(action: "Jump" | "Descend" | "ToolAction" | "Inventory" | "Chat" | "DropItem" | "ChangePerspective"): void;
  cancelKeyCapture(): void;
  closeBrushPalette(): void;
  selectBrushDye(id: string | null): void;
  closeInventory(): void;
  closeStorageBox(): void;
  clickStorageBoxSlot(index: number): void;
  sortStorageBox(): void;
  closeChat(): void;
  submitChat(text: string): void;
  clickInventorySlot(index: number): void;
  sortInventory(): void;
  discardInventoryCursor(): void;
  pickCreativeBlock(choice: InventoryCatalogEntry): void;
  craftInventoryRecipe(recipeId: string): void;
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
  const interactionRoot = useRef<HTMLDivElement>(null);

  useEffect(() => {
    document.documentElement.classList.toggle(
      "mouse-captured",
      state.mouseCaptured,
    );
  }, [state.mouseCaptured]);

  const preWorldVisible =
    state.worldCreation.visible;
  const uiVisible = preWorldVisible || state.navigation.overlay !== "none" ||
    state.chat.open;

  // On an explicit UI screen change, the previous screen's focused button
  // can linger during ScreenTransition's exit animation. Move keyboard focus
  // to the stable App surface immediately instead of waiting for that button
  // to unmount; otherwise Escape is lost to document.body.
  useEffect(() => {
    if (!uiVisible || state.chat.open) return;
    interactionRoot.current?.focus({ preventScroll: true });
  }, [uiVisible, state.navigation.overlay, state.navigation.preWorldScreen,
      state.chat.open]);

  const handleUiKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if (event.key !== "Escape" || event.defaultPrevented ||
        event.repeat || event.nativeEvent.isComposing || !uiVisible) return;
    if (state.settings.captureAction) actions.cancelKeyCapture();
    else if (state.chat.open) actions.closeChat();
    else if (state.navigation.overlay === "inventory") actions.closeInventory();
    else if (state.navigation.overlay === "storage") actions.closeStorageBox();
    else if (state.navigation.overlay === "brush") actions.closeBrushPalette();
    else actions.escapeNavigation();
    event.preventDefault();
    event.stopPropagation();
  };

  return (
    <div ref={interactionRoot} className="app-interaction-root"
      tabIndex={-1} onKeyDown={handleUiKeyDown}
      onBlurCapture={event => {
        // A child editor can blur itself on Escape. Return keyboard focus to
        // this explicit UI surface, not document/window; avoid stealing focus
        // when the user switches away from the application.
        if (uiVisible && !event.relatedTarget && document.hasFocus())
          interactionRoot.current?.focus({ preventScroll: true });
      }}>
      {!preWorldVisible && !state.loading &&
        state.navigation.overlay === "none" && (
        <ChatDock
          open={state.chat.open}
          visible={state.chat.visible}
          history={state.chat.history}
          commands={state.chat.commands}
          catalog={state.chat.catalog}
          onClose={actions.closeChat}
          onSubmit={actions.submitChat}
        />
      )}
      <GameHudPage
        embedded={embedded}
        state={state.hud}
        catalog={state.inventory.catalog}
        onPing={actions.ping}
        onDismissToast={actions.dismissToast}
      />

      {preWorldVisible && (
        <ScreenTransition screenKey={state.navigation.preWorldScreen}>
          {state.navigation.preWorldScreen === "starting" && (
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
            onLoad={actions.loadWorld}
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

        </ScreenTransition>
      )}

      {!preWorldVisible &&
        state.navigation.overlay === "pause" && (
          <PauseMenuPage
            worldSettingsAvailable={Boolean(state.settings.world)}
            gameSettingsAvailable={Boolean(state.settings.client)}
            controlsAvailable={Boolean(state.settings.client)}
            onResume={actions.resumeGame}
            onSaveWorld={actions.saveWorld}
            saveFeedback={
              state.navigation.saveFeedback === "saved"
                ? t("ui.worldSaved")
                : state.navigation.saveFeedback === "error"
                  ? t("ui.worldSaveFailed")
                  : ""
            }
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
          onSpawnCreatures={actions.setSpawnCreatures}
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
          health={state.hud.vitals?.health}
          onClose={actions.closeInventory}
          onSlotClick={actions.clickInventorySlot}
          onSort={actions.sortInventory}
          onDiscardCursor={actions.discardInventoryCursor}
          onCreativePick={actions.pickCreativeBlock}
          onCraft={actions.craftInventoryRecipe}
        />
      )}

      {!preWorldVisible && state.navigation.overlay === "storage" &&
        state.storageBox.open && (
        <StorageBoxPage
          storage={state.storageBox}
          inventory={state.inventory}
          onClose={actions.closeStorageBox}
          onStorageSlotClick={actions.clickStorageBoxSlot}
          onInventorySlotClick={actions.clickInventorySlot}
          onSortStorage={actions.sortStorageBox}
          onSortInventory={actions.sortInventory}
        />
      )}

      <LoadingOverlay state={state.loading} />
    </div>
  );
}
