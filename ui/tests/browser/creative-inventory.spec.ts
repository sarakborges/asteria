import { expect, test } from "@playwright/test";

const url = "/iframe.html?id=pages-inventorygameplaypage--authored-creative-categories&viewMode=story";

async function openCreative(page: import("@playwright/test").Page) {
  await page.goto(url);
  await page.getByRole("navigation", { name: "Inventory" })
    .getByRole("button", { name: "Creative" }).click();
  await expect(page.locator(".creative-inventory-panel")).toBeVisible();
}

test("Creative uses original category order, translated labels and category PNGs", async ({ page }) => {
  await openCreative(page);
  const categories = page.locator(".creative-inventory-panel__category-list");
  await expect(categories.getByRole("button")).toHaveText([
    "Everything", "Stone Blocks", "Tools",
  ]);
  await expect(categories.locator("button img")).toHaveCount(3);
  await categories.getByRole("button", { name: "Tools" }).click();
  await expect(categories.getByRole("button", { name: "Tools" })).toHaveAttribute("aria-pressed", "true");
  await expect(page.locator(".creative-inventory-panel__grid .inventory-slot")).toHaveCount(3);
});

test("Creative search matches localized names and metadata variants", async ({ page }) => {
  await openCreative(page);
  const search = page.getByRole("textbox", { name: "Search creative items" });
  await search.fill("water");
  const grid = page.locator(".creative-inventory-panel__grid");
  await expect(grid.locator(".inventory-slot")).toHaveCount(1);
  await expect(grid.locator(".inventory-slot")).toHaveAttribute("aria-label", /Water/);
  await search.fill("stone_37");
  await expect(grid.locator(".inventory-slot")).toHaveCount(1);
  await expect(grid.locator(".inventory-slot")).toHaveAttribute("aria-label", /Stone 37/i);
});

test("Creative retains scroll separately per category and across tab switching", async ({ page }) => {
  await openCreative(page);
  const categories = page.locator(".creative-inventory-panel__category-list");
  const grid = page.locator(".creative-inventory-panel__grid");
  await categories.getByRole("button", { name: "Stone Blocks" }).click();
  // The story contains one authored stone and 100 generated variants.
  await expect(grid.locator(".inventory-slot")).toHaveCount(101);

  await grid.evaluate(element => { element.scrollTop = 320; element.dispatchEvent(new Event("scroll")); });
  await expect.poll(() => grid.evaluate(element => element.scrollTop)).toBeGreaterThan(0);
  await categories.getByRole("button", { name: "Tools" }).click();
  await expect.poll(() => grid.evaluate(element => element.scrollTop)).toBe(0);
  await categories.getByRole("button", { name: "Stone Blocks" }).click();
  await expect.poll(() => grid.evaluate(element => element.scrollTop)).toBeGreaterThan(0);

  await page.getByRole("navigation", { name: "Inventory" })
    .getByRole("button", { name: "Inventory" }).click();
  await page.getByRole("navigation", { name: "Inventory" })
    .getByRole("button", { name: "Creative" }).click();
  await expect.poll(() => page.locator(".creative-inventory-panel__grid")
    .evaluate(element => element.scrollTop)).toBeGreaterThan(0);
});
