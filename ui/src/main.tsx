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
import { createWorldCreationController } from "./controllers/WorldCreationController";
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

subscribeGodotMessages((message) => {
  hud.handleGodotMessage(message);
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
      openWorldCreation: navigation.openWorldCreation,
      backToStart: navigation.backToStart,
      exitGame: navigation.exitGame,
      dismissToast: store.dismissToast,
    }}
  />
  </LocalizationProvider>,
);

postGodotMessage("ui.ready", {
  version: 1,
});
