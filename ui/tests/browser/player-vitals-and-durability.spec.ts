import { expect, test } from "@playwright/test";

test("underwater vitals show real oxygen independently of health", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-playervitals--underwater&viewMode=story");
  const oxygen = page.locator(".hud-entity-card__health--oxygen");
  await expect(oxygen).toBeVisible();
  await expect(oxygen).toHaveAttribute("aria-valuenow", "6");
  await expect(oxygen).toHaveAttribute("aria-valuemax", "15");
  await expect(oxygen).toContainText("Oxygen");
  await expect(page.locator(".hud-entity-card__health--health"))
    .toHaveAttribute("aria-valuenow", "20");
});

test("breath reaching zero remains visible until surfacing", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-playervitals--drowning&viewMode=story");
  await expect(page.locator(".hud-entity-card__health--oxygen"))
    .toHaveAttribute("aria-valuenow", "0");

  await page.goto("/iframe.html?id=organisms-playervitals--respawned&viewMode=story");
  await expect(page.locator(".hud-entity-card__health--oxygen")).toHaveCount(0);
});

test("damaged armor shows its remaining uses and a wear bar", async ({ page }) => {
  await page.goto("/iframe.html?id=molecules-inventoryslot--damaged-armor&viewMode=story");
  const slot = page.locator(".inventory-slot");
  await expect(slot.locator(".inventory-slot__durability-fill")).toBeVisible();
  await expect(slot).toHaveAttribute("aria-label", /Durability: 71 \/ 240/);
  await slot.hover();
  await expect(page.locator(".item-tooltip")).toContainText("Durability: 71 / 240");
});
