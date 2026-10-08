import { test, expect } from "@playwright/test";

test("storage box renders the exact 27-slot container and player inventory", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-storageboxpage--default&viewMode=story");
  const storage = page.locator(".storage-box-page__grid").first();
  const backpack = page.locator(".storage-box-page__grid").last();
  await expect(storage.locator("button")).toHaveCount(27);
  await expect(backpack.locator("button")).toHaveCount(27);
  await expect(page.locator(".inventory-hotbar-footer__slots button")).toHaveCount(9);
  await expect(page.getByRole("button", { name: "Sort storage" })).toBeEnabled();
  await expect(page.getByRole("button", { name: "Sort backpack" })).toBeEnabled();

  await page.getByRole("textbox", { name: "Search storage" }).fill("never-matches");
  await expect(storage.locator("button:enabled")).toHaveCount(0);
  await expect(backpack.locator("button:enabled")).toHaveCount(27);
});

test("storage cursor and full-inventory feedback have dedicated UI states", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-storageboxpage--full-cursor&viewMode=story");
  await expect(page.locator(".inventory-cursor-overlay")).toHaveCount(1);
  await expect(page.getByRole("status")).toContainText("Inventory full");
});
