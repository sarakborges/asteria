import { expect, test } from "@playwright/test";
import { createUiStore } from "../../src/state/uiStore";
import { createInitialUiState } from "../../src/state/uiState";
import { createUiNavigationController } from "../../src/controllers/UiNavigationController";

test("native death state blocks Escape and waits for explicit respawn completion", () => {
  const posted: string[] = [];
  const store = createUiStore(createInitialUiState(false));
  const navigation = createUiNavigationController(store, type => posted.push(type));

  navigation.handleGodotMessage({ type: "game.player.death", payload: {
    keepInventory: false, droppedStacks: 9, dropCapacityExceeded: false,
  } });
  expect(store.getSnapshot().navigation.overlay).toBe("death");
  expect(store.getSnapshot().navigation.death?.droppedStacks).toBe(9);
  navigation.escape();
  navigation.handleGodotMessage({ type: "game.mouse_capture", payload: {
    captured: true,
  } });
  expect(store.getSnapshot().navigation.overlay).toBe("death");
  expect(posted).toEqual([]);

  navigation.respawn();
  expect(posted).toEqual(["ui.player.respawn"]);
  expect(store.getSnapshot().navigation.overlay).toBe("death");

  navigation.handleGodotMessage({ type: "game.player.respawned" });
  expect(store.getSnapshot().navigation.overlay).toBe("none");
  expect(store.getSnapshot().navigation.death).toBeNull();
});

test("death page offers manual respawn and explains inventory disposition", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-deathscreenpage--items-dropped&viewMode=story");
  await expect(page.getByRole("heading", { name: "You Died" })).toBeVisible();
  await expect(page.getByRole("button", { name: "Respawn" })).toBeVisible();
  await expect(page.locator(".death-screen__explanation"))
    .toContainText("Your items were dropped");
});

test("capacity fallback clearly reports that possessions were preserved", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-deathscreenpage--capacity-protected&viewMode=story");
  await expect(page.locator(".death-screen__explanation"))
    .toContainText("inventory was kept");
});

test("save-and-leave is available while dead without clearing death on failure", () => {
  const posted: string[] = [];
  const store = createUiStore(createInitialUiState(false));
  const navigation = createUiNavigationController(store, type => posted.push(type));
  navigation.handleGodotMessage({ type: "game.player.death", payload: {
    keepInventory: true, droppedStacks: 0,
    dropCapacityExceeded: false, outcomeKnown: true,
  } });
  navigation.leaveWorld();
  expect(posted).toEqual(["ui.world.leave"]);
  expect(store.getSnapshot().navigation.death).not.toBeNull();

  navigation.handleGodotMessage({
    type: "game.world.save_result", payload: { status: "error" },
  });
  expect(store.getSnapshot().navigation.saveFeedback).toBe("error");
  expect(store.getSnapshot().navigation.death).not.toBeNull();

  navigation.handleGodotMessage({ type: "game.world.left" });
  expect(store.getSnapshot().navigation.death).toBeNull();
});

test("restored-death screen never fabricates a prior item-drop outcome", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-deathscreenpage--restored-dead-save&viewMode=story");
  await expect(page.getByRole("heading", { name: "You Died" })).toBeVisible();
  await expect(page.locator(".death-screen__explanation"))
    .toContainText("restored after your death");
  await expect(page.locator(".death-screen__explanation"))
    .not.toContainText("Your items were dropped");
  await expect(page.getByRole("button", { name: "Leave World" })).toBeVisible();
});

test("death screen surfaces a failed save while retaining manual respawn", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-deathscreenpage--exit-save-failed&viewMode=story");
  await expect(page.getByRole("alert")).toContainText("Failed");
  await expect(page.getByRole("button", { name: "Respawn" })).toBeVisible();
});
