import "./styles/global.css";
import {
  isGodotEmbedded,
  postGodotMessage,
  subscribeGodotMessages,
} from "./bridge/godotBridge";
import { createGameHudPage } from "./components/pages/GameHudPage";
import { createHudController } from "./controllers/HudController";

const root = document.querySelector<HTMLDivElement>("#app");
if (!root) throw new Error("Missing #app root");

const page = createGameHudPage({
  embedded: isGodotEmbedded(),
});

root.replaceChildren(page.element);

const controller = createHudController(page, postGodotMessage);
controller.mount();
subscribeGodotMessages(controller.handleGodotMessage);

postGodotMessage("ui.ready", { version: 1 });
