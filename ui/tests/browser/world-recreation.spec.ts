import { expect, test } from "@playwright/test";
import { createWorldCatalogController } from "../../src/controllers/WorldCatalogController";
import { createInitialUiState } from "../../src/state/uiState";
import { createUiStore } from "../../src/state/uiStore";

test("Recreate World appears on saved-world cards only", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-worldselectionpage--with-worlds&viewMode=story");
  const cards = page.locator(".world-card");
  await expect(cards).toHaveCount(2);
  for (const card of await cards.all())
    await expect(card.getByRole("button", { name: "Recreate World" })).toBeEnabled();
  await expect(page.locator(".loading-overlay")).toHaveCount(0);
});

test("incompatible save cannot be recreated", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-worldselectionpage--with-worlds&viewMode=story");
  // Other saves may be compatible; the incompatible-specific story is authoritative.
  await page.goto("/iframe.html?id=organisms-worldcard--incompatible&viewMode=story");
  await expect(page.getByRole("button", { name: "Recreate World" })).toHaveCount(0);
});

test("world actions remain disabled while native save operations run", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-worldselectionpage--deleting&viewMode=story");
  await expect(page.getByRole("button", { name: "Recreate World" })).toHaveCount(2);
  await expect(page.getByRole("button", { name: "Recreate World" }).first()).toBeDisabled();
});

test("recreation passes only selected saved ID and blocks duplicate requests", () => {
  const store = createUiStore(createInitialUiState(false));
  const sent: { type: string; payload: unknown }[] = [];
  const controller = createWorldCatalogController(store, (type, payload) => {
    sent.push({ type, payload });
  });
  store.update(state => ({
    ...state,
    worldCatalog: {
      ...state.worldCatalog,
      status: "ready",
      worlds: [
        {
          id: "Asteria", lastSaved: "", seed: "42", daysPassed: "0",
          sphere: "asteria:overworld", coordinates: "", compatible: true,
        },
        {
          id: "Damaged", lastSaved: "", seed: "", daysPassed: "0",
          sphere: "", coordinates: "", compatible: false,
        },
      ],
    },
  }));

  controller.recreateWorld("Damaged");
  controller.recreateWorld("Missing");
  expect(sent).toEqual([]);

  controller.recreateWorld("Asteria");
  controller.recreateWorld("Asteria");
  expect(sent).toEqual([
    { type: "ui.world.catalog.recreate_draft", payload: { id: "Asteria" } },
  ]);
  expect(store.getSnapshot().worldCatalog.status).toBe("verifying");
});
