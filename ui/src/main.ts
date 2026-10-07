import "./styles/global.css";
import {
  isGodotEmbedded,
  postGodotMessage,
  subscribeGodotMessages,
} from "./bridge/godotBridge";
import { createGameHudPage } from "./components/pages/GameHudPage";
import { createNewWorldPage } from "./components/pages/NewWorldPage";
import { createHudController } from "./controllers/HudController";
import { createWorldCreationController } from "./controllers/WorldCreationController";

const root = document.querySelector<HTMLDivElement>("#app");
if (!root) throw new Error("Missing #app root");

const page = createGameHudPage({
  embedded: isGodotEmbedded(),
});

const newWorld = createNewWorldPage();
root.replaceChildren(page.element, newWorld.element);

const controller = createHudController(page, postGodotMessage);
const worldCreationController = createWorldCreationController(
  newWorld,
  postGodotMessage,
);
controller.mount();
worldCreationController.mount();
subscribeGodotMessages((message) => {
  controller.handleGodotMessage(message);
  worldCreationController.handleGodotMessage(message);
});

postGodotMessage("ui.ready", { version: 1 });
