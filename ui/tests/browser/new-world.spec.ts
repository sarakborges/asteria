import { test, expect, type Page } from "@playwright/test";

const STORY = "/iframe.html?id=pages-newworldpage--";
const shell = ".new-world .settings-page";
const sections = ".settings-page__section";

async function expectActualWorldSettings(page: Page) {
  await expect(page.locator(shell)).toBeVisible();

  const nav = page.locator(".new-world .settings-page__navigation");
  const content = page.locator(".new-world .settings-page__sections");
  await expect(nav).toBeVisible();
  await expect(content).toBeVisible();
  await expect(nav.locator("button")).toHaveCount(2);
  await expect(page.locator(sections)).toHaveCount(2);

  const worldSettings = page.locator(sections).first();
  const gameRules = page.locator(sections).last();
  const labels = nav.locator("button");
  await expect(worldSettings.locator(".ui-text--heading").first()).toHaveText(
    (await labels.nth(0).innerText()).trim(),
  );
  await expect(gameRules.locator(".ui-text--heading").first()).toHaveText(
    (await labels.nth(1).innerText()).trim(),
  );

  // Exact MineClone hierarchy: new_world_settings_section() contains
  // world_name_setting, seed_setting, world_settings_section (game mode).
  const nameInput = worldSettings.locator(".setting-row input").first();
  const seed = worldSettings.locator("#world-seed");
  const mode = worldSettings.locator(".game-mode-picker");
  await expect(nameInput).toBeVisible();
  await expect(seed).toBeVisible();
  await expect(mode).toBeVisible();
  await expect(gameRules.locator("#world-seed")).toHaveCount(0);

  const nameRect = await nameInput.boundingBox();
  const seedRect = await seed.boundingBox();
  const modeRect = await mode.boundingBox();
  expect(nameRect && seedRect && modeRect).toBeTruthy();
  expect(nameRect!.y).toBeLessThan(seedRect!.y);
  expect(seedRect!.y).toBeLessThan(modeRect!.y);

  await expect(gameRules.locator(".numeric-stepper")).toHaveCount(1);
  await expect(gameRules.getByRole("switch")).toHaveCount(1);

  return { nav, content, worldSettings, gameRules };
}

test("Create New World preserves the MineClone sidebar and section ownership", async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  await page.goto(STORY + "ready&viewMode=story");
  const { nav, content } = await expectActualWorldSettings(page);

  const navRect = await nav.boundingBox();
  const panelRect = await content.boundingBox();
  expect(navRect && panelRect).toBeTruthy();
  expect(navRect!.width).toBeGreaterThanOrEqual(275);
  expect(navRect!.x + navRect!.width).toBeLessThan(panelRect!.x);

  await page.screenshot({ path: "test-results/new-world-desktop.png", fullPage: true });
});

test("actual pre-world App navigation reaches the full new-world screen", async ({ page }) => {
  await page.setViewportSize({ width: 1920, height: 1080 });
  await page.goto(STORY + "real-navigation&viewMode=story");
  await expect(page.locator(".starting-screen__button").first()).toBeVisible();
  await page.locator(".starting-screen__button").first().click();
  await expect(page.locator(".world-selection-screen")).toBeVisible();
  await page.locator(".world-selection__footer-button").last().click();
  const { nav, content } = await expectActualWorldSettings(page);

  const navRect = await nav.boundingBox();
  const panelRect = await content.boundingBox();
  expect(navRect && panelRect).toBeTruthy();
  expect(navRect!.x + navRect!.width).toBeLessThan(panelRect!.x);

  await page.screenshot({ path: "test-results/new-world-real-navigation.png", fullPage: true });
});

test("seed accepts uint64 maximum without Number precision loss", async ({ page }) => {
  await page.goto(STORY + "ready&viewMode=story");
  const seed = page.locator("#world-seed");
  await expect(seed).toBeVisible();
  await seed.fill("18446744073709551615");
  await expect(seed).toHaveValue("18446744073709551615");
  await seed.fill("18446744073709551616");
  await expect(seed).toHaveValue("18446744073709551615");
});

test("640px game window retains MineClone left navigation", async ({ page }) => {
  await page.setViewportSize({ width: 640, height: 900 });
  await page.goto(STORY + "ready&viewMode=story");
  const { nav, content } = await expectActualWorldSettings(page);
  const navRect = await nav.boundingBox();
  const panelRect = await content.boundingBox();
  expect(navRect && panelRect).toBeTruthy();
  expect(navRect!.x + navRect!.width).toBeLessThan(panelRect!.x);
  await page.screenshot({ path: "test-results/new-world-narrow.png", fullPage: true });
});

test("Game Rules respects the existing Spawn Creatures flag", async ({ page }) => {
  await page.goto(STORY + "no-creature-spawning&viewMode=story");
  const { gameRules } = await expectActualWorldSettings(page);
  await expect(gameRules.getByRole("switch")).toHaveAttribute("aria-checked", "false");
});
