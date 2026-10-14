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
import { SaveFailureExitDialog } from "./components/organisms/SaveFailureExitDialog/SaveFailureExitDialog";
import { DeathScreenPage } from "./components/pages/DeathScreenPage/DeathScreenPage";
import { useLocalization } from "./localization/LocalizationProvider";
import { BrushPalettePage } from "./components/pages/BrushPalettePage/BrushPalettePage";
import { NewWorldPage } from "./components/pages/NewWorldPage/NewWorldPage";
import { WorldSelectionPage } from "./components/pages/WorldSelectionPage/WorldSelectionPage";
import { AddonsPage } from "./components/pages/AddonsPage/AddonsPage";
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
    keepInventory: boolean;
    creatureAttackMode: import("./state/uiState").CreatureAttackMode;
    generation: import("./state/uiState").WorldGenerationDraft;
  }): void;
  randomizeWorld(): void;
  openWorldSelection(): void;
  openAddons(tab: "mods" | "packs"): void;
  refreshAddons(): void;
  setPackScope(name: string, scope: "Data" | "Resources" | "Both" | null): void;
  movePack(name: string, direction: -1 | 1): void;
  configureMod(id: string, enabled: boolean, settings: string): void;
  openSavesFolder(): void;
  loadWorld(id: string): void;
  deleteWorld(id: string): void;
  recreateWorld(id: string): void;
  openWorldCreation(): void;
  backToStart(): void;
  exitGame(): void;
  confirmExitWithoutSave(): void;
  cancelExitWithoutSave(): void;
  dismissToast(id: number): void;
  resumeGame(): void;
  respawn(): void;
  leaveWorld(): void;
  openGameSettings(): void;
  openWorldSettings(): void;
  openControls(): void;
  backFromOverlay(): void;
  escapeNavigation(): void;
  setRenderDistance(value: number): void;
  setTargetPosition(value: "Center" | "TopRight" | "Hidden"): void;
  setHideHud(value: boolean): void;
  setHideHints(value: boolean): void;
  setGameplayHint(kind: "RotateBlock" | "BreakOrPlaceBlock", value: boolean): void;
  setWorldTicks(value: number): void;
  setSpawnCreatures(value: boolean): void;
  setCreatureAttackMode(value: import("./state/uiState").CreatureAttackMode): void;
  setKeepInventory(value: boolean): void;
  setGameMode(value: import("./state/uiState").GameMode): void;
  beginKeyCapture(action: "Jump" | "Descend" | "ToolAction" | "Inventory" | "Chat" | "DropItem" | "ChangePerspective"): void;
  cancelKeyCapture(): void;
  closeBrushPalette(): void;
  selectBrushDye(id: string | null): void;
  closeInventory(shortcut?: boolean): void;
  closeStorageBox(): void;
  clickStorageBoxSlot(index: number, action: import("./components/molecules/InventorySlot/InventorySlot").InventorySlotAction): void;
  sortStorageBox(): void;
  closeChat(): void;
  submitChat(text: string): void;
  activateChatMessage(messageId: string): void;
  clickInventorySlot(index: number, action: import("./components/molecules/InventorySlot/InventorySlot").InventorySlotAction): void;
  clickEquipmentSlot(index: number): void;
  sortInventory(): void;
  discardInventoryCursor(): void;
  clearInventory(): void;
  dropInventoryCursor(): void;
  pickCreativeBlock(choice: InventoryCatalogEntry, fillStack: boolean): void;
  craftInventoryRecipe(recipeId: string): void;
  rotateCharacterPortrait(deltaX: number): void;
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
    state.chat.open || state.navigation.death !== null ||
    state.navigation.exitConfirmation;

  // On an explicit UI screen change, the previous screen's focused button
  // can linger during ScreenTransition's exit animation. Move keyboard focus
  // to the stable App surface immediately instead of waiting for that button
  // to unmount; otherwise Escape is lost to document.body.
  useEffect(() => {
    if (!uiVisible || state.chat.open) return;
    interactionRoot.current?.focus({ preventScroll: true });
  }, [uiVisible, state.navigation.overlay, state.navigation.preWorldScreen,
      state.chat.open]);

  const navigateEscape = () => {
    if (state.navigation.exitConfirmation) actions.cancelExitWithoutSave();
    else if (state.settings.captureAction) actions.cancelKeyCapture();
    else if (state.chat.open) actions.closeChat();
    else if (state.navigation.overlay === "inventory") actions.closeInventory();
    else if (state.navigation.overlay === "storage") actions.closeStorageBox();
    else if (state.navigation.overlay === "brush") actions.closeBrushPalette();
    else actions.escapeNavigation();
  };

  const handleUiKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    if ((state.navigation.death && !state.navigation.exitConfirmation) ||
        !uiVisible) return;

    // WRY forwards document-level keydown to Godot. Stop every inventory
    // shortcut event before that listener, including auto-repeat and typing
    // in a focused editor. Only the initial non-editor press closes the modal.
    if (state.navigation.overlay === "inventory" &&
        event.code === (state.settings.client?.keybinds.inventory ?? "KeyE")) {
      event.stopPropagation();
      if (event.defaultPrevented || event.repeat ||
          event.nativeEvent.isComposing ||
          (event.target instanceof Element && event.target.closest(
            "input, textarea, select, [contenteditable='true']"))) return;
      actions.closeInventory(true);
      event.preventDefault();
      return;
    }

    if (event.defaultPrevented || event.repeat ||
        event.nativeEvent.isComposing || event.key !== "Escape") return;
    navigateEscape();
    event.preventDefault();
    event.stopPropagation();
  };

  // WRY can leave DOM focus on body after an OS/native focus handoff.
  // Recover modal-only close shortcuts when no App descendant has focus;
  // keys inside the modal stay owned by the explicit React surface.
  useEffect(() => {
    const onWindowKeyDown = (event: globalThis.KeyboardEvent) => {
      if ((state.navigation.death && !state.navigation.exitConfirmation) ||
          !uiVisible) return;
      const inUiSurface = event.target instanceof Node &&
        Boolean(interactionRoot.current?.contains(event.target));
      if (state.navigation.overlay === "inventory" &&
          !inUiSurface &&
          event.code === (state.settings.client?.keybinds.inventory ?? "KeyE")) {
        event.stopPropagation();
        if (event.defaultPrevented || event.repeat || event.isComposing) return;
        actions.closeInventory(true);
        event.preventDefault();
        return;
      }
      if (event.defaultPrevented || event.repeat || event.isComposing ||
          event.key !== "Escape" ||
          (state.navigation.overlay !== "pause" && inUiSurface)) return;
      navigateEscape();
      event.preventDefault();
      event.stopPropagation();
    };
    window.addEventListener("keydown", onWindowKeyDown, true);
    return () => window.removeEventListener("keydown", onWindowKeyDown, true);
  }, [actions, uiVisible, state.navigation.death, state.navigation.exitConfirmation,
      state.navigation.overlay, state.settings.captureAction, state.settings.client?.keybinds.inventory,
      state.chat.open]);

  return (
    <div ref={interactionRoot} className="app-interaction-root"
      tabIndex={-1} onKeyDown={handleUiKeyDown}
      onBlurCapture={event => {
        // A child editor can blur itself on Escape. Return keyboard focus to
        // this explicit UI surface, not document/window; avoid stealing focus
        // when the user switches away from the application.
        if (uiVisible && !state.chat.open && !event.relatedTarget && document.hasFocus())
          interactionRoot.current?.focus({ preventScroll: true });
      }}>
      {!preWorldVisible && !state.loading &&
        state.navigation.overlay === "none" && (
        <ChatDock
          open={state.chat.open}
          visible={state.chat.visible}
          focusRequest={state.chat.focusRequest}
          history={state.chat.history}
          commands={state.chat.commands}
          catalog={state.chat.catalog}
          onClose={actions.closeChat}
          onSubmit={actions.submitChat}
          onMessageAction={actions.activateChatMessage}
        />
      )}
      {!state.settings.client?.hud.hideHud && (
        <GameHudPage
          embedded={embedded}
          state={state.hud}
          catalog={state.inventory.catalog}
          onPing={actions.ping}
          onDismissToast={actions.dismissToast}
        />
      )}

      {!preWorldVisible && !state.loading && state.navigation.death && (
        <DeathScreenPage
          keepInventory={state.navigation.death.keepInventory}
          droppedStacks={state.navigation.death.droppedStacks}
          dropCapacityExceeded={state.navigation.death.dropCapacityExceeded}
          outcomeKnown={state.navigation.death.outcomeKnown}
          saveError={state.navigation.saveFeedback === "error"}
          onRespawn={actions.respawn}
          onSaveAndLeave={actions.leaveWorld}
        />
      )}

      {/* Keep one transition instance alive across pause, settings and controls.
          Do not intercept gameplay resume, inventory/chat, or native ESC. */}
      {(preWorldVisible || state.navigation.overlay === "pause" ||
        state.navigation.overlay === "game" ||
        state.navigation.overlay === "world" ||
        state.navigation.overlay === "controls") && (
        <ScreenTransition screenKey={
          state.navigation.overlay === "none"
            ? `preworld:${state.navigation.preWorldScreen}`
            : `menu:${state.navigation.overlay}`
        }>
          {preWorldVisible && state.navigation.overlay === "none" && (
            <>
          {state.navigation.preWorldScreen === "starting" && (
          <StartingScreenPage
            onPlay={actions.openWorldSelection}
            onMods={() => { actions.openAddons("mods"); actions.refreshAddons(); }}
            onPacks={() => { actions.openAddons("packs"); actions.refreshAddons(); }}
            onExit={actions.exitGame}
            settingsAvailable={Boolean(state.settings.client)}
            controlsAvailable={Boolean(state.settings.client)}
            onSettings={actions.openGameSettings}
            onControls={actions.openControls}
          />
        )}

      {preWorldVisible &&
        (state.navigation.preWorldScreen === "mods" ||
         state.navigation.preWorldScreen === "packs") && (
        <AddonsPage
          tab={state.navigation.preWorldScreen}
          state={state.addons}
          onBack={actions.backToStart}
          onPackScope={actions.setPackScope}
          onMovePack={actions.movePack}
          onModConfigure={actions.configureMod}
        />
      )}

      {preWorldVisible &&
        state.navigation.preWorldScreen ===
          "world-selection" && (
          <WorldSelectionPage
            worlds={state.worldCatalog.worlds}
            status={
              state.worldCatalog.status === "deleting"
                ? t("worldSelection.deleting")
                : state.worldCatalog.status === "verifying"
                ? t("worldSelection.verifying")
                : state.worldCatalog.status === "unavailable"
                  ? t("worldSelection.unavailable")
                  : state.worldCatalog.status === "ready" &&
                      state.worldCatalog.worlds.length === 0
                    ? t("worldSelection.noRestorable")
                    : undefined
            }
            error={
              state.worldCatalog.deleteError
                ? t("worldSelection.deleteError")
                : state.worldCatalog.loadError
                ? t("worldSelection.loadError")
                : state.worldCatalog.status === "error"
                ? t("worldSelection.scanError")
                : state.worldCatalog.folderError
                  ? t("worldSelection.openSavesFolderError")
                  : undefined
            }
            onBack={actions.backToStart}
            onCreateWorld={actions.openWorldCreation}
            onOpenSavesFolder={actions.openSavesFolder}
            onLoad={actions.loadWorld}
            onDelete={actions.deleteWorld}
            onRecreate={actions.recreateWorld}
            busy={state.worldCatalog.status !== "ready"}
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

            </>
          )}

      {!preWorldVisible && !state.navigation.death &&
        state.navigation.overlay === "pause" && (
          <PauseMenuPage
            worldSettingsAvailable={Boolean(state.settings.world)}
            gameSettingsAvailable={Boolean(state.settings.client)}
            controlsAvailable={Boolean(state.settings.client)}
            onResume={actions.resumeGame}
            onLeaveWorld={actions.leaveWorld}
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

      {!state.navigation.death && (state.navigation.overlay === "game" ||
        state.navigation.overlay === "world") && (
        <SettingsWorkspacePage
          scope={state.navigation.overlay}
          settings={state.settings}
          onBack={actions.backFromOverlay}
          onRenderDistance={actions.setRenderDistance}
          onTargetPosition={actions.setTargetPosition}
          onHideHud={actions.setHideHud}
          onHideHints={actions.setHideHints}
          onGameplayHint={actions.setGameplayHint}
          onWorldTicks={actions.setWorldTicks}
          onSpawnCreatures={actions.setSpawnCreatures}
          onCreatureAttackMode={actions.setCreatureAttackMode}
          onKeepInventory={actions.setKeepInventory}
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

        </ScreenTransition>
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
          onEquipmentClick={actions.clickEquipmentSlot}
          onSort={actions.sortInventory}
          onDiscardCursor={actions.discardInventoryCursor}
          onClearInventory={actions.clearInventory}
          onDropCursor={actions.dropInventoryCursor}
          onCreativePick={actions.pickCreativeBlock}
          onCraft={actions.craftInventoryRecipe}
          onRotatePortrait={actions.rotateCharacterPortrait}
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
      {state.navigation.exitConfirmation && (
        <SaveFailureExitDialog
          onCancel={actions.cancelExitWithoutSave}
          onConfirm={actions.confirmExitWithoutSave}
        />
      )}
    </div>
  );
}
