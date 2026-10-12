import { expect, test } from "@playwright/test";

test("loading screen never offers Recreate World", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-loadingoverlay--materializing&viewMode=story");
  await expect(page.locator(".loading-overlay")).toBeVisible();
  await expect(page.getByRole("button", { name: "Recreate World" })).toHaveCount(0);
});

test("retiring dimension loading has no recreation button", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-loadingoverlay--retiring-dimension&viewMode=story");
  await expect(page.getByRole("button", { name: "Recreate World" })).toHaveCount(0);
});
