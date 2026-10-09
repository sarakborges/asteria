import "./styles/global.css";
import { LocalizationProvider } from "./localization/LocalizationProvider";
import { createRoot } from "react-dom/client";
import { App } from "./App";
import {
  isGodotEmbedded,
  postGodotMessage,
  subscribeGodotMessages,
} from "./bridge/godotBridge";
import { createHudController } from "./controllers/HudController";
import { createLoadingController } from "./controllers/LoadingController";
import { createUiNavigationController } from "./controllers/UiNavigationController";
import { createInventoryController } from "./controllers/InventoryController";
import { createStorageBoxController } from "./controllers/StorageBoxController";
import { createBrushController } from "./controllers/BrushController";
import { createSettingsController } from "./controllers/SettingsController";
import { createWorldCreationController } from "./controllers/WorldCreationController";
import { createWorldCatalogController } from "./controllers/WorldCatalogController";
import { createChatController } from "./controllers/ChatController";
import { createInitialUiState } from "./state/uiState";
import { createUiStore } from "./state/uiStore";

const rootElement =
  document.querySelector<HTMLDivElement>("#app");
if (!rootElement) {
  throw new Error("Missing #app root");
}

const embedded = isGodotEmbedded();
const store = createUiStore(
  createInitialUiState(embedded),
);
const hud = createHudController(
  store,
  postGodotMessage,
);
const loading = createLoadingController(store);
const navigation =
  createUiNavigationController(
    store,
    postGodotMessage,
  );
const worldCreation =
  createWorldCreationController(
    store,
    postGodotMessage,
  );
const settings = createSettingsController(store, postGodotMessage);
const inventory = createInventoryController(store, postGodotMessage);
const storageBox = createStorageBoxController(store, postGodotMessage);
const brush = createBrushController(store, postGodotMessage);
const worldCatalog = createWorldCatalogController(store, postGodotMessage);
const chat = createChatController(store, postGodotMessage);

subscribeGodotMessages((message) => {
  navigation.handleGodotMessage(message);
  worldCatalog.handleGodotMessage(message);
  inventory.handleGodotMessage(message);
  storageBox.handleGodotMessage(message);
  chat.handleGodotMessage(message);
  brush.handleGodotMessage(message);
  hud.handleGodotMessage(message);
  settings.handleGodotMessage(message);
  worldCreation.handleGodotMessage(message);
  loading.handleGodotMessage(message);
});

createRoot(rootElement).render(
  <LocalizationProvider>
  <App
    embedded={embedded}
    store={store}
    actions={{
      ping: hud.ping,
      createWorld: worldCreation.createWorld,
      randomizeWorld: worldCreation.randomizeWorld,
      openWorldSelection: navigation.openWorldSelection,
      openSavesFolder: worldCatalog.openSavesFolder,
      loadWorld: worldCatalog.loadWorld,
      deleteWorld: worldCatalog.deleteWorld,
      openWorldCreation: navigation.openWorldCreation,
      backToStart: navigation.backToStart,
      exitGame: navigation.exitGame,
      dismissToast: store.dismissToast,
      resumeGame: navigation.resumeGame,
      saveWorld: navigation.saveWorld,
      leaveWorld: navigation.leaveWorld,
      openGameSettings: navigation.openGameSettings,
      openWorldSettings: navigation.openWorldSettings,
      openControls: navigation.openControls,
      backFromOverlay: navigation.backFromOverlay,
      escapeNavigation: navigation.escape,
      setRenderDistance: settings.setRenderDistance,
      setTargetPosition: settings.setTargetPosition,
      setHideHints: settings.setHideHints,
      setGameplayHint: settings.setGameplayHint,
      setWorldTicks: settings.setWorldTicks,
      setSpawnCreatures: settings.setSpawnCreatures,
      setGameMode: settings.setGameMode,
      beginKeyCapture: settings.beginKeyCapture,
      cancelKeyCapture: settings.cancelKeyCapture,
      closeBrushPalette: brush.close,
      selectBrushDye: brush.select,
      closeInventory: inventory.close,
      closeStorageBox: storageBox.close,
      clickStorageBoxSlot: storageBox.clickSlot,
      sortStorageBox: storageBox.sort,
      closeChat: chat.close,
      submitChat: chat.submit,
      clickInventorySlot: inventory.clickSlot,
      clickEquipmentSlot: inventory.clickEquipment,
      sortInventory: inventory.sort,
      discardInventoryCursor: inventory.discardCursor,
      pickCreativeBlock: inventory.pickCreative,
      craftInventoryRecipe: inventory.craft,
      rotateCharacterPortrait: inventory.rotatePortrait,
    }}
  />
  </LocalizationProvider>,
);

postGodotMessage("ui.ready", {
  version: 1,
});
