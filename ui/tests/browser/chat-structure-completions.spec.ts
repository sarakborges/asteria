import { expect, test } from "@playwright/test";
import type { BridgeMessage } from "../../src/bridge/godotBridge";
import { createChatController } from "../../src/controllers/ChatController";
import { createInitialUiState } from "../../src/state/uiState";
import { createUiStore } from "../../src/state/uiStore";

const story = "/iframe.html?id=organisms-chatdock--open&viewMode=story";

test("place offers authored structures while locate hides nonlocatable ones", async ({ page }) => {
  await page.goto(story);
  const input = page.locator(".chat-panel__input input");

  await input.fill("/place structure ruins");
  await expect(page.getByRole("option").filter({ hasText: "asteria:ruins" }))
    .toBeVisible();

  await input.fill("/locate structure ruins");
  await expect(page.getByRole("option").filter({ hasText: "asteria:ruins" }))
    .toHaveCount(0);

  await input.fill("/locate structure village");
  await expect(page.getByRole("option").filter({ hasText: "asteria:village" }))
    .toBeVisible();

  // Do not drop the valid place-only variations.
  await input.fill("/place structure ruins 2");
  await expect(page.getByRole("option").filter({ hasText: "2" }))
    .toBeVisible();
});

test("bridge rejects invented or invalid locate candidates atomically", () => {
  const store = createUiStore(createInitialUiState(true));
  const chat = createChatController(store, () => {});
  const catalog = {
    creatures: [], biomes: [], structures: ["asteria:allowed", "asteria:place_only"],
    locatableStructures: ["asteria:allowed"],
    variations: [], dimensions: [], position: null,
  };
  const payload = {
    open: true, visible: true, history: [], commands: ["/place", "/locate"], catalog,
  };
  chat.handleGodotMessage({ type: "game.chat.state", payload } satisfies BridgeMessage);
  expect(store.getSnapshot().chat.catalog.structures)
    .toEqual(catalog.structures);
  expect(store.getSnapshot().chat.catalog.locatableStructures)
    .toEqual(["asteria:allowed"]);

  // An invalid native snapshot must not replace the accepted state.
  chat.handleGodotMessage({ type: "game.chat.state", payload: {
    ...payload, catalog: { ...catalog, locatableStructures: ["asteria:invented"] },
  }} satisfies BridgeMessage);
  expect(store.getSnapshot().chat.catalog.locatableStructures)
    .toEqual(["asteria:allowed"]);
});
