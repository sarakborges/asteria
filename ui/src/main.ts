import "./styles/global.css";
import {
  isGodotEmbedded,
  postGodotMessage,
  subscribeGodotMessages,
} from "./bridge/godotBridge";
import { createGameHudPage } from "./components/pages/GameHudPage";
import { createNewWorldPage } from "./components/pages/NewWorldPage";
import { createLoadingOverlay } from "./components/organisms/LoadingOverlay";
import { createHudController } from "./controllers/HudController";
import { createLoadingController } from "./controllers/LoadingController";
import { createWorldCreationController } from "./controllers/WorldCreationController";

const root = document.querySelector<HTMLDivElement>("#app");
if (!root) throw new Error("Missing #app root");

const page = createGameHudPage({
  embedded: isGodotEmbedded(),
});

const newWorld = createNewWorldPage();
const loading = createLoadingOverlay();
root.replaceChildren(
  page.element,
  newWorld.element,
  loading.element,
);

const controller = createHudController(page, postGodotMessage);
const loadingController = createLoadingController(
  loading,
);
const worldCreationController = createWorldCreationController(
  newWorld,
  postGodotMessage,
);
controller.mount();
worldCreationController.mount();
subscribeGodotMessages((message) => {
  controller.handleGodotMessage(message);
  worldCreationController.handleGodotMessage(message);
  loadingController.handleGodotMessage(message);
});

postGodotMessage("ui.ready", { version: 1 });
