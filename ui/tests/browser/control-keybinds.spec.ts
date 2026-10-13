import { test, expect } from "@playwright/test";

test("controls expose the actual remappable inventory, chat and perspective keys", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-controlspage--full-asteria-controls&viewMode=story");
  await expect(page.locator(".controls-page")).toBeVisible();
  const text = page.locator(".controls-page").getByText("Change Perspective", { exact: true });
  await expect(text).toBeVisible();
  await expect(page.locator(".controls-page").getByText("Inventory", { exact: true })).toBeVisible();
  // Chat is both a section title and an editable key action; scope the
  // assertion to the actual Chat category rather than an ambiguous label.
  const chatGroup = page.locator(".controls-page__group").last();
  await expect(chatGroup.locator(".ui-text--heading")).toHaveText("Chat");
  await expect(chatGroup.getByText("Chat", { exact: true }).last()).toBeVisible();
  await expect(page.locator(".controls-page").getByText("F5", { exact: true })).toBeVisible();
});
