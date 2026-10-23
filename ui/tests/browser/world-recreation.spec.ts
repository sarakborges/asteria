import { expect, test } from "@playwright/test";
import { createWorldCatalogController } from "../../src/controllers/WorldCatalogController";
import { createWorldCreationController } from "../../src/controllers/WorldCreationController";
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
          id: "Asteria", lastSavedUnixMs: null, seed: "42", daysPassed: "0",
          sphere: "asteria:overworld", coordinates: "", compatible: true,
        },
        {
          id: "Damaged", lastSavedUnixMs: null, seed: "", daysPassed: "0",
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


test("recreation draft restores saved settings and opens world creation", () => {
  const store = createUiStore(createInitialUiState(false));
  const controller = createWorldCreationController(store, () => undefined);

  controller.handleGodotMessage({
    type: "game.world_creation",
    payload: {
      isRecreationDraft: true,
      seed: "18446744073709551615",
      name: "Copy of Asteria",
      mode: "Creative",
      ticksPerSecond: 60,
      spawnCreatures: false,
      keepInventory: false,
      creatureAttackMode: "Peaceful",
      generation: {
        mode: "Flat",
        spawnBiome: "asteria:overworld/plains",
        biomeSizeTenths: 25,
        spawnStructures: false,
        singleBiome: true,
        spawnCaves: false,
        spawnOceans: false,
      },
      spawnBiomes: [{ id: "asteria:overworld/plains", label: "Plains" }],
    },
  });

  const state = store.getSnapshot();
  expect(state.navigation.preWorldScreen).toBe("new-world");
  expect(state.worldCatalog.status).toBe("ready");
  expect(state.worldCreation.visible).toBe(true);
  expect(state.worldCreation.seed).toBe("18446744073709551615");
  expect(state.worldCreation.name).toBe("Copy of Asteria");
  expect(state.worldCreation.mode).toBe("Creative");
  expect(state.worldCreation.ticksPerSecond).toBe("60");
  expect(state.worldCreation.spawnCreatures).toBe(false);
  expect(state.worldCreation.keepInventory).toBe(false);
  expect(state.worldCreation.creatureAttackMode).toBe("Peaceful");
  expect(state.worldCreation.generation).toEqual({
    mode: "Flat",
    spawnBiome: "asteria:overworld/plains",
    biomeSizeTenths: 25,
    spawnStructures: false,
    singleBiome: true,
    spawnCaves: false,
    spawnOceans: false,
  });
});
