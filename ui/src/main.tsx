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
import { createBrushController } from "./controllers/BrushController";
import { createSettingsController } from "./controllers/SettingsController";
import { createWorldCreationController } from "./controllers/WorldCreationController";
import { createWorldCatalogController } from "./controllers/WorldCatalogController";
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
const brush = createBrushController(store, postGodotMessage);
const worldCatalog = createWorldCatalogController(store, postGodotMessage);

subscribeGodotMessages((message) => {
  navigation.handleGodotMessage(message);
  worldCatalog.handleGodotMessage(message);
  inventory.handleGodotMessage(message);
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
      openWorldCreation: navigation.openWorldCreation,
      backToStart: navigation.backToStart,
      exitGame: navigation.exitGame,
      dismissToast: store.dismissToast,
      resumeGame: navigation.resumeGame,
      openGameSettings: navigation.openGameSettings,
      openWorldSettings: navigation.openWorldSettings,
      openControls: navigation.openControls,
      backFromOverlay: navigation.backFromOverlay,
      setRenderDistance: settings.setRenderDistance,
      setTargetPosition: settings.setTargetPosition,
      setHideHints: settings.setHideHints,
      setGameplayHint: settings.setGameplayHint,
      setWorldTicks: settings.setWorldTicks,
      setGameMode: settings.setGameMode,
      beginKeyCapture: settings.beginKeyCapture,
      cancelKeyCapture: settings.cancelKeyCapture,
      closeBrushPalette: brush.close,
      selectBrushDye: brush.select,
      closeInventory: inventory.close,
      clickInventorySlot: inventory.clickSlot,
      sortInventory: inventory.sort,
      discardInventoryCursor: inventory.discardCursor,
      pickCreativeBlock: inventory.pickCreative,
    }}
  />
  </LocalizationProvider>,
);

postGodotMessage("ui.ready", {
  version: 1,
});
