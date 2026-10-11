import { expect, test } from "@playwright/test";

test("personal station remains visible without available recipes", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-inventorygameplaypage--no-recipes&viewMode=story");
  const station = page.locator(".current-station-panel");
  await expect(station).toBeVisible();
  await expect(station).toContainText("Personal crafting");
  await expect(station.locator(".current-station-panel__icon-frame img")).toHaveCount(1);
  await expect(page.locator(".crafting-panel")).toContainText("No recipes available.");
});

test("crafting displays output capacity separately from ingredient availability", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-inventorygameplaypage--recipe-output-full&viewMode=story");
  const crafting = page.locator(".crafting-panel");
  await expect(crafting).toContainText("Not enough inventory space.");
  await expect(crafting.getByRole("button", { name: "Craft" })).toBeDisabled();
  await expect(crafting).not.toContainText("Missing materials");
});
