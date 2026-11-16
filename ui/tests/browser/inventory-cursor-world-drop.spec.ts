import { expect, test } from "@playwright/test";
import { createInventoryController } from "../../src/controllers/InventoryController";
import { createInitialUiState } from "../../src/state/uiState";
import { createUiStore } from "../../src/state/uiStore";

test("blank inventory backdrop releases the cursor, not slots or buttons", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-inventorygameplaypage--cursor-drop-interactions&viewMode=story");
  const count = page.getByTestId("world-drop-count");
  const backdrop = page.locator(".inventory-gameplay");
  await expect(count).toHaveText("0");
  await expect(page.locator(".inventory-cursor-overlay")).toBeAttached();

  // Right-click and normal inventory controls do not drop the stack.
  await backdrop.click({ position: { x: 5, y: 5 }, button: "right" });
  await page.locator(".inventory-gameplay__actions button").click();
  await page.locator(".player-inventory-panel .inventory-slot").first().click();
  await expect(count).toHaveText("0");

  // Left-click outside the panels releases exactly one complete cursor stack.
  await backdrop.click({ position: { x: 5, y: 5 } });
  await expect(count).toHaveText("1");
  await expect(page.locator(".inventory-cursor-overlay")).toHaveCount(0);
  await backdrop.click({ position: { x: 5, y: 5 } });
  await expect(count).toHaveText("1");
});

test("inventory controller publishes drop only for held cursors and validates failure feedback", () => {
  const store = createUiStore(createInitialUiState(true));
  const sent: { type: string; payload?: unknown }[] = [];
  const controller = createInventoryController(store,
    (type, payload) => sent.push({ type, payload }));

  controller.dropCursor();
  expect(sent).toEqual([]);
  store.update(state => ({ ...state, inventory: {
    ...state.inventory, open: true,
    cursor: { id: "asteria:stick", kind: "item", quantity: 17, metadata: {} },
  }}));
  controller.dropCursor();
  expect(sent).toEqual([{ type: "ui.inventory.drop_cursor", payload: undefined }]);

  controller.handleGodotMessage({
    type: "game.inventory.error", payload: { code: "DropCapacityExceeded" },
  });
  expect(store.getSnapshot().inventory.errorKey)
    .toBe("inventory.error.dropsFull");

  store.update(state => ({ ...state, inventory: {
    ...state.inventory, open: false,
  }}));
  controller.dropCursor();
  expect(sent).toHaveLength(1);
});
