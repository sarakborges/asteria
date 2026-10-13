import { expect, test } from "@playwright/test";
import { createWorldCatalogController } from "../../src/controllers/WorldCatalogController";
import { createInitialUiState } from "../../src/state/uiState";
import { createUiStore } from "../../src/state/uiStore";

const story = "/iframe.html?id=pages-worldselectionpage--with-worlds&viewMode=story";

test("delete confirmation captures keyboard and Escape restores the triggering Delete button", async ({ page }) => {
  await page.goto(story);
  const first = page.locator(".world-card").first();
  const trigger = first.getByRole("button", { name: "Delete" });
  await trigger.click();
  const dialog = page.getByRole("alertdialog", { name: "Delete World" });
  await expect(dialog).toBeVisible();
  const back = dialog.getByRole("button", { name: "Back" });
  const commit = dialog.getByRole("button", { name: "Delete" });
  await expect(back).toBeFocused();
  await expect(first.getByRole("button", { name: "Load" })).toBeDisabled();

  // Tab is contained between the two confirmation choices, not other cards.
  await page.keyboard.press("Shift+Tab");
  await expect(commit).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(back).toBeFocused();

  await page.keyboard.press("Escape");
  await expect(dialog).toHaveCount(0);
  await expect(trigger).toBeFocused();
  await expect(first.getByRole("button", { name: "Load" })).toBeEnabled();

  await trigger.press("Enter");
  await expect(back).toBeFocused();
  await back.click();
  await expect(trigger).toBeFocused();
});

test("saved world catalog rejects incompatible loads and locks duplicate native intents", () => {
  const store = createUiStore(createInitialUiState(false));
  const sent: { type: string; payload?: unknown }[] = [];
  const controller = createWorldCatalogController(store, (type, payload) => {
    sent.push({ type, payload });
  });
  const worlds = [
    { id: "Asteria", lastSaved: "", seed: "42", daysPassed: "0",
      sphere: "asteria:overworld", coordinates: "", compatible: true },
    { id: "Damaged", lastSaved: "", seed: "", daysPassed: "",
      sphere: "", coordinates: "", compatible: false },
  ];
  store.update(s => ({
    ...s,
    worldCatalog: { ...s.worldCatalog, status: "ready", worlds },
  }));

  controller.loadWorld("Damaged");
  controller.loadWorld("Unknown");
  controller.loadWorld("../Asteria");
  expect(sent).toEqual([]);

  controller.loadWorld("Asteria");
  controller.loadWorld("Asteria");
  controller.deleteWorld("Asteria");
  expect(sent).toEqual([
    { type: "ui.world.catalog.load", payload: { id: "Asteria" } },
  ]);
  expect(store.getSnapshot().worldCatalog.status).toBe("verifying");

  // Godot reports a failed native restore: the list must be usable for retry,
  // without hiding it behind the generic catalog-scanning error state.
  controller.handleGodotMessage({ type: "game.world_catalog.load_error",
    payload: { error: "Corrupted save" } });
  expect(store.getSnapshot().worldCatalog.status).toBe("ready");
  expect(store.getSnapshot().worldCatalog.loadError).toBe(true);
  expect(store.getSnapshot().worldCatalog.worlds).toEqual(worlds);

  controller.loadWorld("Asteria");
  expect(sent).toHaveLength(2);
  expect(store.getSnapshot().worldCatalog.loadError).toBe(false);
});

test("failed world delete keeps the catalog and permits exactly one retry", () => {
  const store = createUiStore(createInitialUiState(false));
  const sent: { type: string; payload?: unknown }[] = [];
  const controller = createWorldCatalogController(store, (type, payload) =>
    sent.push({ type, payload }));
  store.update(s => ({
    ...s,
    worldCatalog: { ...s.worldCatalog, status: "ready", worlds: [
      { id: "Damaged", lastSaved: "", seed: "", daysPassed: "",
        sphere: "", coordinates: "", compatible: false },
    ] },
  }));

  controller.deleteWorld("Missing");
  expect(sent).toHaveLength(0);
  controller.deleteWorld("Damaged");
  controller.deleteWorld("Damaged");
  controller.loadWorld("Damaged");
  expect(sent).toEqual([
    { type: "ui.world.catalog.delete", payload: { id: "Damaged" } },
  ]);
  expect(store.getSnapshot().worldCatalog.status).toBe("deleting");

  controller.handleGodotMessage({ type: "game.world_catalog.delete_result",
    payload: { status: "error" } });
  expect(store.getSnapshot().worldCatalog.status).toBe("ready");
  expect(store.getSnapshot().worldCatalog.deleteError).toBe(true);
  expect(store.getSnapshot().worldCatalog.worlds).toHaveLength(1);

  controller.deleteWorld("Damaged");
  expect(sent).toHaveLength(2);
  expect(store.getSnapshot().worldCatalog.deleteError).toBe(false);
  controller.handleGodotMessage({ type: "game.world_catalog.delete_result",
    payload: { status: "deleting" } });
  controller.handleGodotMessage({ type: "game.world_catalog",
    payload: { status: "ready", worlds: [] } });
  expect(store.getSnapshot().worldCatalog.worlds).toHaveLength(0);
  expect(store.getSnapshot().worldCatalog.status).toBe("ready");
});
