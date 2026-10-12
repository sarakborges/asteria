import { expect, test } from "@playwright/test";

test("loading exposes Recreate World without opening another form", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-loadingoverlay--materializing&viewMode=story");
  const overlay = page.locator(".loading-overlay");
  await expect(overlay).toBeVisible();
  const recreate = overlay.getByRole("button", { name: "Recreate World" });
  await expect(recreate).toBeEnabled();
  await recreate.click();
  await expect(overlay).toBeVisible();
});

test("recreate is disabled while the previous session retires", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-loadingoverlay--retiring-dimension&viewMode=story");
  await expect(page.getByRole("button", { name: "Recreate World" })).toBeDisabled();
});
