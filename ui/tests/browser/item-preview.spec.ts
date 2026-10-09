import { test, expect } from "@playwright/test";

test("the same authored block preview appears in selected hotbar and target HUD", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-gamehudpage--runtime&viewMode=story");
  const target = page.locator(".target-hud");
  const selectedSlot = page.locator(".hotbar-slot--selected");
  await expect(target.locator(".item-glyph--cube")).toHaveCount(1);
  await expect(selectedSlot.locator(".item-glyph--cube")).toHaveCount(1);
  await expect(page.locator(".hotbar__selected-name")).toContainText("Grass Block");
});

test("the standalone cube preview has textured faces instead of an abbreviated ID", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-targethud--textured-block&viewMode=story");
  const icon = page.locator(".target-hud__slot .item-glyph--cube");
  await expect(icon).toBeVisible();
  await expect(icon.locator(".item-glyph__cube-face")).toHaveCount(3);
  await expect(page.locator(".target-hud__slot")).not.toContainText("GRA");
});
