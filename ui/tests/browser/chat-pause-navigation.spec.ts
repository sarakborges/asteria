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
      locatableStructures: [],
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

test("window focus loss explicitly opens pause even if capture was already lost", () => {
  const { store, receive } = createGameplayBridge();
  store.update(state => ({ ...state, mouseCaptured: false }));
  receive({ type: "game.ui.pause", payload: {} });
  expect(store.getSnapshot().navigation.overlay).toBe("pause");

  // A subsequent capture-loss event must not close the open pause.
  receive({ type: "game.mouse_capture", payload: { captured: false } });
  expect(store.getSnapshot().navigation.overlay).toBe("pause");
});

test("focus-loss pause does not override an existing modal or open chat", () => {
  const { store, receive } = createGameplayBridge();
  receive(openChat);
  receive({ type: "game.ui.pause", payload: {} });
  expect(store.getSnapshot().navigation.overlay).toBe("none");

  store.update(state => ({
    ...state,
    chat: { ...state.chat, open: false },
    navigation: { ...state.navigation, overlay: "world" },
  }));
  receive({ type: "game.ui.pause", payload: {} });
  expect(store.getSnapshot().navigation.overlay).toBe("world");
});

test("focus-loss pause never opens on the pre-world screen", () => {
  const { store, receive } = createGameplayBridge();
  store.update(state => ({
    ...state,
    worldCreation: { ...state.worldCreation, visible: true },
  }));
  receive({ type: "game.ui.pause", payload: {} });
  expect(store.getSnapshot().navigation.overlay).toBe("none");
});

test("modal close after OS focus loss produces a visible pause, not an orphaned cursor", () => {
  const { store, receive } = createGameplayBridge();
  receive(openChat);
  expect(store.getSnapshot().chat.open).toBe(true);

  // Native focus loss while chat is open should not replace the chat with
  // pause. After chat submits/closes, an unfocused player cannot recapture:
  // Main publishes an explicit game.ui.pause after game.chat.state closes.
  receive({ type: "game.ui.pause", payload: {} });
  expect(store.getSnapshot().navigation.overlay).toBe("none");

  receive({
    type: "game.chat.state",
    payload: { ...(openChat.payload as Record<string, unknown>), open: false, visible: false },
  });
  receive({ type: "game.ui.pause", payload: {} });
  expect(store.getSnapshot().chat.open).toBe(false);
  expect(store.getSnapshot().navigation.overlay).toBe("pause");

  // A repeated native capture-loss or focus event must not close pause.
  receive({ type: "game.mouse_capture", payload: { captured: false } });
  expect(store.getSnapshot().navigation.overlay).toBe("pause");
});

test("unfocused inventory close can hand off to pause without resuming gameplay", () => {
  const { store, receive } = createGameplayBridge();
  store.update(state => ({
    ...state,
    navigation: { ...state.navigation, overlay: "inventory" },
    inventory: { ...state.inventory, open: true },
  }));
  receive({ type: "game.ui.pause", payload: {} });
  expect(store.getSnapshot().navigation.overlay).toBe("inventory");

  // The inventory controller closes its own overlay when the native
  // inventory state arrives; Main only sends focus-loss pause afterward.
  store.update(state => ({
    ...state,
    navigation: { ...state.navigation, overlay: "none" },
    inventory: { ...state.inventory, open: false },
  }));
  receive({ type: "game.ui.pause", payload: {} });
  expect(store.getSnapshot().navigation.overlay).toBe("pause");
});

test("late focus-loss pause never replaces an already restored gameplay capture", () => {
  const { store, receive } = createGameplayBridge();
  receive(openChat);
  receive({
    type: "game.chat.state",
    payload: { ...(openChat.payload as Record<string, unknown>), open: false, visible: false },
  });
  // If the player resumed while the window was focused, native capture
  // wins; the host must not send an extra pause event in that scenario.
  receive({ type: "game.mouse_capture", payload: { captured: true } });
  expect(store.getSnapshot().navigation.overlay).toBe("none");
  expect(store.getSnapshot().mouseCaptured).toBe(true);
});

test("native Escape opens pause if gameplay is uncaptured without a visible overlay", () => {
  const { store, receive } = createGameplayBridge();
  store.update(state => ({ ...state, mouseCaptured: false }));
  expect(store.getSnapshot().navigation.overlay).toBe("none");

  // When mouse capture was already lost, Godot emits game.ui.escape
  // instead of another game.mouse_capture(false). This must not be a no-op.
  receive({ type: "game.ui.escape", payload: {} });
  expect(store.getSnapshot().navigation.overlay).toBe("pause");
});

test("native Escape does not open pause behind an active chat input", () => {
  const { store, receive } = createGameplayBridge();
  receive(openChat);
  receive({ type: "game.ui.escape", payload: {} });
  expect(store.getSnapshot().chat.open).toBe(true);
  expect(store.getSnapshot().navigation.overlay).toBe("none");
});

test("native Escape in pre-world screens cannot open a gameplay pause", () => {
  const { store, receive } = createGameplayBridge();
  store.update(state => ({
    ...state,
    worldCreation: { ...state.worldCreation, visible: true },
    navigation: { ...state.navigation, overlay: "none", preWorldScreen: "starting" },
  }));
  receive({ type: "game.ui.escape", payload: {} });
  expect(store.getSnapshot().navigation.overlay).toBe("none");
});
