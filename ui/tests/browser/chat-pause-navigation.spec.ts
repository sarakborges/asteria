import { expect, test } from "@playwright/test";
import type { BridgeMessage } from "../../src/bridge/godotBridge";
import { createChatController } from "../../src/controllers/ChatController";
import { createHudController } from "../../src/controllers/HudController";
import { createUiNavigationController } from "../../src/controllers/UiNavigationController";
import { createInitialUiState } from "../../src/state/uiState";
import { createUiStore } from "../../src/state/uiStore";

const openChat: BridgeMessage = {
  type: "game.chat.state",
  payload: {
    open: true,
    visible: true,
    history: [],
    commands: [],
    catalog: {
      creatures: [],
      biomes: [],
      structures: [],
      dimensions: [],
      variations: [],
      position: null,
    },
  },
};

function createGameplayBridge() {
  const store = createUiStore(createInitialUiState(true));
  store.update(state => ({
    ...state,
    mouseCaptured: true,
    worldCreation: { ...state.worldCreation, visible: false },
  }));
  const navigation = createUiNavigationController(store, () => {});
  const chat = createChatController(store, () => {});
  const hud = createHudController(store, () => {});

  // Same controller dispatch order as ui/src/main.tsx.
  const receive = (message: BridgeMessage) => {
    navigation.handleGodotMessage(message);
    chat.handleGodotMessage(message);
    hud.handleGodotMessage(message);
  };

  return { store, receive };
}

test("T opens chat without entering pause when chat state precedes mouse release", () => {
  const { store, receive } = createGameplayBridge();

  receive(openChat);
  receive({ type: "game.mouse_capture", payload: { captured: false } });

  expect(store.getSnapshot().chat.open).toBe(true);
  expect(store.getSnapshot().mouseCaptured).toBe(false);
  expect(store.getSnapshot().navigation.overlay).toBe("none");
});

test("late chat state clears a pause opened by an earlier capture-loss event", () => {
  const { store, receive } = createGameplayBridge();

  receive({ type: "game.mouse_capture", payload: { captured: false } });
  expect(store.getSnapshot().navigation.overlay).toBe("pause");

  receive(openChat);
  expect(store.getSnapshot().chat.open).toBe(true);
  expect(store.getSnapshot().navigation.overlay).toBe("none");
});

test("releasing capture without chat still opens pause", () => {
  const { store, receive } = createGameplayBridge();

  receive({ type: "game.mouse_capture", payload: { captured: false } });

  expect(store.getSnapshot().navigation.overlay).toBe("pause");
});

test("native focus request survives arriving before the chat state", () => {
  const { store, receive } = createGameplayBridge();
  receive({ type: "game.chat.focus_input", payload: {} });
  expect(store.getSnapshot().chat.focusRequest).toBe(1);
  receive(openChat);
  expect(store.getSnapshot().chat.open).toBe(true);
  expect(store.getSnapshot().chat.focusRequest).toBe(1);
});

test("native focus acknowledgment requests the chat input again", () => {
  const { store, receive } = createGameplayBridge();

  receive(openChat);
  receive({ type: "game.chat.focus_input", payload: {} });
  expect(store.getSnapshot().chat.focusRequest).toBe(1);

  // A subsequent chat presentation update must not discard focus intent.
  receive(openChat);
  expect(store.getSnapshot().chat.focusRequest).toBe(1);
});

test("ESC closes the pause overlay without clicking it", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-newworldpage--modal-navigation&viewMode=story");
  await expect(page.locator(".pause-menu")).toBeVisible();
  // WebView often leaves focus on document.body after capture is released.
  await page.evaluate(() => (document.activeElement as HTMLElement)?.blur());
  await page.keyboard.press("Escape");
  await expect(page.locator(".pause-menu")).toHaveCount(0);
});
