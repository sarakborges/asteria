import { expect, test } from "@playwright/test";
import { createInventoryController } from "../../src/controllers/InventoryController";
import { createInitialUiState } from "../../src/state/uiState";
import { createUiStore } from "../../src/state/uiStore";

const icon = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=";

const payload = {
  everythingIconUrl: icon,
  categories: [
    { id: "tools", order: 50, iconUrl: icon },
    { id: "stone_blocks", order: 10, iconUrl: icon },
  ],
  items: [
    {
      id: "asteria:stone", kind: "block", name: "asteria:stone",
      category: "stone_blocks", metadata: {}, iconUrl: icon,
    },
    {
      id: "asteria:bucket", kind: "tool", name: "asteria:bucket",
      category: "tools", metadata: { contained_fluid: "asteria:water" }, iconUrl: icon,
    },
  ],
};

test("creative catalog bridge preserves authentic categories, order, icons and metadata", () => {
  const store = createUiStore(createInitialUiState(false));
  const inventory = createInventoryController(store, () => {});
  inventory.handleGodotMessage({ type: "game.inventory.catalog", payload });

  const state = store.getSnapshot().inventory;
  expect(state.categories.map(category => category.id))
    .toEqual(["stone_blocks", "tools"]);
  expect(state.categories.map(category => category.iconUrl))
    .toEqual([icon, icon]);
  expect(state.everythingIconUrl).toBe(icon);
  expect(state.catalog[1].metadata).toEqual({ contained_fluid: "asteria:water" });
});

test("malformed creative catalog is rejected as a whole without changing current UI state", () => {
  const store = createUiStore(createInitialUiState(false));
  const inventory = createInventoryController(store, () => {});
  inventory.handleGodotMessage({ type: "game.inventory.catalog", payload });
  const prior = store.getSnapshot().inventory;
  const invalid = [
    { ...payload, categories: [payload.categories[0], payload.categories[0]] },
    { ...payload, everythingIconUrl: "javascript:alert(1)" },
    { ...payload, items: [{ ...payload.items[0], category: "unknown" }] },
    { ...payload, categories: [{ ...payload.categories[0], iconUrl: "https://untrusted.test/a.png" }] },
    { ...payload, items: [{ ...payload.items[1],
      metadata: { contained_fluid: "x".repeat(1000) } }] },
  ];
  for (const candidate of invalid) {
    inventory.handleGodotMessage({ type: "game.inventory.catalog", payload: candidate });
    expect(store.getSnapshot().inventory).toBe(prior);
  }
});
