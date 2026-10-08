import { expect, test } from "@playwright/test";

const story = (name: string) =>
  "/iframe.html?id=pages-newworldpage--" + name + "&viewMode=story";

test("Escape goes back one pre-world screen without closing a seed editor's parent", async ({ page }) => {
  await page.goto(story("real-navigation"));
  await expect(page.locator(".starting-screen")).toBeVisible();
  await page.locator(".starting-screen__button").first().click();
  await expect(page.locator(".world-selection-screen")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.locator(".starting-screen")).toBeVisible();

  await page.locator(".starting-screen__button").first().click();
  await page.locator(".world-selection__footer-button").last().click();
  await expect(page.locator(".new-world .settings-page")).toBeVisible();

  const seed = page.locator("#world-seed");
  const original = await seed.inputValue();
  await seed.fill("123");
  await seed.press("Escape");
  await expect(seed).toHaveValue(original);
  await expect(page.locator(".new-world .settings-page")).toBeVisible();

  await page.keyboard.press("Escape");
  await expect(page.locator(".world-selection-screen")).toBeVisible();
});

test("Escape closes game settings to pause and pause to gameplay", async ({ page }) => {
  await page.goto(story("modal-navigation"));
  await expect(page.locator(".pause-menu")).toBeVisible();
  await page.locator(".pause-menu__settings-row button").last().click();
  await expect(page.locator(".settings-workspace .settings-page")).toBeVisible();

  await page.keyboard.press("Escape");
  await expect(page.locator(".pause-menu")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.locator(".pause-menu")).toHaveCount(0);
});

test("Escape closes inventory through the same injected semantic action as its Close button", async ({ page }) => {
  await page.goto(story("inventory-navigation"));
  await expect(page.locator(".inventory-gameplay")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.locator(".inventory-gameplay")).toHaveCount(0);
});

test("Escape dismisses a focused dropdown before it navigates away", async ({ page }) => {
  await page.goto(story("modal-navigation"));
  await page.locator(".pause-menu__settings-row button").last().click();
  await expect(page.locator(".settings-workspace .settings-page")).toBeVisible();

  const dropdown = page.locator(".settings-workspace .ui-dropdown__control").first();
  await dropdown.click();
  await expect(dropdown).toHaveAttribute("aria-expanded", "true");
  await page.keyboard.press("Escape");
  await expect(dropdown).toHaveAttribute("aria-expanded", "false");
  await expect(page.locator(".settings-workspace .settings-page")).toBeVisible();

  await page.keyboard.press("Escape");
  await expect(page.locator(".pause-menu")).toBeVisible();
});
