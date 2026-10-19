import { expect, test } from "@playwright/test";

const hud = (name: string) =>
  `/iframe.html?id=pages-gamehudpage--${name}&viewMode=story`;
const inventory = (name: string) =>
  `/iframe.html?id=organisms-playerinventorypanel--${name}&viewMode=story`;

test("HUD previews the active attached layer rather than the underlying cube", async ({ page }) => {
  await page.goto(hud("active-attached-layer"));
  const target = page.locator(".target-hud");
  await expect(target).toBeVisible();
  await expect(target.locator(".target-hud__copy strong")).toContainText(/moss/i);
  await expect(target.locator(".target-hud__copy span").first()).toContainText(/grass/i);
  await expect(target.locator(".target-hud__slot .item-glyph--recipe")).toHaveCount(1);
  await expect(target.locator(".target-hud__slot img.item-glyph")).toBeVisible();
  await expect(target.locator(".target-hud__slot .item-glyph--cube")).toHaveCount(0);
  await expect(target.locator(".target-hud__layers")).toHaveCount(0);
});

test("HUD still renders a block preview if layer icon is unavailable", async ({ page }) => {
  await page.goto(hud("unavailable-layer-icon"));
  const target = page.locator(".target-hud");
  await expect(target).toBeVisible();
  await expect(target.locator(".target-hud__slot .item-glyph--cube")).toHaveCount(1);
  await expect(target.locator(".target-hud__slot img.item-glyph")).toHaveCount(0);
  await expect(target.locator(".target-hud__copy strong")).toContainText(/unknown layer/i);
  await expect(target.locator(".target-hud__layers")).toHaveCount(0);
});

test("localized inventory search visibly dims and disables only nonmatching slots", async ({ page }) => {
  await page.goto(inventory("localized-search-filtering"));
  const panel = page.locator(".player-inventory-panel");
  const backpack = panel.locator(".player-inventory-panel__backpack .inventory-slot");
  const hotbar = panel.locator(".inventory-hotbar-footer__slots .inventory-slot");
  await expect(backpack).toHaveCount(27);
  await expect(hotbar).toHaveCount(9);

  await expect(backpack.nth(0)).not.toBeDisabled();
  await expect(backpack.nth(0)).not.toHaveClass(/inventory-slot--filtered/);
  await expect(backpack.nth(1)).toBeDisabled();
  await expect(backpack.nth(1)).toHaveClass(/inventory-slot--filtered/);
  await expect(backpack.nth(2)).toHaveClass(/inventory-slot--filtered/);

  await expect(hotbar.nth(0)).not.toBeDisabled();
  await expect(hotbar.nth(0)).not.toHaveClass(/inventory-slot--filtered/);
  await expect(hotbar.nth(1)).toBeDisabled();
  await expect(hotbar.nth(1)).toHaveClass(/inventory-slot--filtered/);

  const matchedOpacity = await backpack.nth(0).evaluate(element =>
    Number(getComputedStyle(element).opacity));
  const filteredOpacity = await backpack.nth(1).evaluate(element =>
    Number(getComputedStyle(element).opacity));
  expect(filteredOpacity).toBeLessThan(matchedOpacity);
});

test("cleared inventory search keeps the complete grid visible without filtered markers", async ({ page }) => {
  await page.goto(inventory("default"));
  const panel = page.locator(".player-inventory-panel");
  await expect(panel.locator(".inventory-slot--filtered")).toHaveCount(0);
  await expect(panel.locator(".player-inventory-panel__backpack .inventory-slot")).toHaveCount(27);
  await expect(panel.locator(".inventory-hotbar-footer__slots .inventory-slot")).toHaveCount(9);
});

test("native tinted hotbar and target layer icons override stale Creative colors", async ({ page }) => {
  await page.goto(hud("native-tinted-hud-icons"));
  const layer = page.locator(".hotbar-slot[data-slot='0'] img.item-glyph");
  const block = page.locator(".hotbar-slot[data-slot='1'] img.item-glyph");
  const targeted = page.locator(".target-hud__slot img.item-glyph");
  await expect(layer).toHaveAttribute("src", "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGPQuBTwHwAEugJKI9WgoAAAAABJRU5ErkJggg==");
  await expect(block).toHaveAttribute("src", "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGN4ZmP0HwAFtAJUX2i7uAAAAABJRU5ErkJggg==");
  await expect(targeted).toHaveAttribute("src", "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGN4ZmP0HwAFtAJUX2i7uAAAAABJRU5ErkJggg==");
  await expect(page.locator(".target-hud__copy strong")).toContainText(/moss/i);
});
