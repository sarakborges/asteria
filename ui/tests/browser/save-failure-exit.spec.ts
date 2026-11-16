import { expect, test } from "@playwright/test";
import { createUiStore } from "../../src/state/uiStore";
import { createInitialUiState } from "../../src/state/uiState";
import { createUiNavigationController } from "../../src/controllers/UiNavigationController";

test("failed exit save requires explicit confirmation and Escape keeps the world open", () => {
  const posted: string[] = [];
  const store = createUiStore(createInitialUiState(false));
  const navigation = createUiNavigationController(store, type => posted.push(type));

  navigation.exitGame();
  expect(posted).toEqual(["ui.app.exit"]);
  navigation.handleGodotMessage({
    type: "game.world.save_result", payload: { status: "error" },
  });
  expect(store.getSnapshot().navigation.exitConfirmation).toBe(false);

  navigation.handleGodotMessage({ type: "game.app.exit_confirmation" });
  expect(store.getSnapshot().navigation.exitConfirmation).toBe(true);
  expect(store.getSnapshot().navigation.saveFeedback).toBe("error");
  navigation.escape();
  expect(store.getSnapshot().navigation.exitConfirmation).toBe(false);
  expect(posted).toEqual(["ui.app.exit", "ui.app.exit.cancel_without_save"]);

  // Only a second explicit approval can send the exit-without-save command.
  navigation.confirmExitWithoutSave();
  expect(posted).toHaveLength(2);
  navigation.handleGodotMessage({ type: "game.app.exit_confirmation" });
  navigation.confirmExitWithoutSave();
  expect(posted.at(-1)).toBe("ui.app.exit.confirm_without_save");
});

test("save failure confirmation is accessible and remains visible above other UI", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-savefailureexitdialog--save-failed&viewMode=story");
  const dialog = page.getByRole("alertdialog", { name: "Save failed" });
  await expect(dialog).toBeVisible();
  await expect(dialog).toContainText("your latest changes will be lost");
  await expect(dialog).toBeFocused();
  await expect(dialog.getByRole("button", { name: "Keep Playing" })).toBeVisible();
  await expect(dialog.getByRole("button", { name: "Exit Without Saving" })).toBeVisible();

  await page.keyboard.press("Tab");
  await expect(dialog.getByRole("button", { name: "Keep Playing" })).toBeFocused();
  await page.keyboard.press("Shift+Tab");
  await expect(dialog.getByRole("button", { name: "Exit Without Saving" })).toBeFocused();
  await page.keyboard.press("Tab");
  await expect(dialog.getByRole("button", { name: "Keep Playing" })).toBeFocused();
});

test("pause menu exposes exit-only saving without Save World", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-pausemenupage--default&viewMode=story");
  await expect(page.locator(".pause-menu")).toBeVisible();
  await expect(page.locator(".pause-menu__actions .ui-button")).toHaveCount(6);
  await expect(page.getByRole("button", { name: "Save World" })).toHaveCount(0);
});
