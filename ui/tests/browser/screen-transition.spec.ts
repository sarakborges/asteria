import { test, expect } from "@playwright/test";

const story = "/iframe.html?id=templates-screentransition--rapid-navigation&viewMode=story";

test("coalesces rapid screen requests and shows only the latest destination", async ({ page }) => {
  await page.goto(story);
  const current = page.getByTestId("transition-page");
  await expect(current).toHaveText("starting");
  await page.getByRole("button", { name: "Navigate rapidly" }).click();
  await expect(current).toHaveText("starting");
  await expect(page.locator(".screen-transition")).toHaveAttribute("aria-busy", "true");
  await expect(current).toHaveText("settings", { timeout: 1800 });
  await expect(page.locator(".screen-transition")).toHaveAttribute("aria-busy", "false");
  await expect(page.locator(".screen-transition__veil")).toHaveCSS("pointer-events", "none");
});

test("reduced motion switches immediately without inserting a fade", async ({ page }) => {
  await page.emulateMedia({ reducedMotion: "reduce" });
  await page.goto(story);
  const current = page.getByTestId("transition-page");
  await page.getByRole("button", { name: "Navigate rapidly" }).click();
  await expect(current).toHaveText("settings", { timeout: 1800 });
  await expect(page.locator(".screen-transition")).toHaveAttribute("aria-busy", "false");
  await expect(page.locator(".screen-transition__veil")).toHaveCSS("opacity", "0");
});

const pauseFlow = "/iframe.html?id=pages-newworldpage--modal-navigation&viewMode=story";

test("pause and game settings use the same fade without delaying ESC navigation", async ({ page }) => {
  await page.goto(pauseFlow);
  const transition = page.locator(".screen-transition");
  await expect(transition).toHaveAttribute("aria-busy", "false");
  await expect(page.locator(".pause-menu")).toBeVisible();

  await page.locator(".pause-menu__settings-row button").last().click();
  await expect(transition).toHaveAttribute("aria-busy", "true");
  // Outgoing menu stays mounted until the veil is fully opaque.
  await expect(page.locator(".pause-menu")).toBeVisible();
  await expect(page.locator(".settings-workspace .settings-page")).toBeVisible();
  await expect(transition).toHaveAttribute("aria-busy", "false");

  await page.keyboard.press("Escape");
  await expect(transition).toHaveAttribute("aria-busy", "true");
  await expect(page.locator(".pause-menu")).toBeVisible();
  await expect(transition).toHaveAttribute("aria-busy", "false");

  // Resume is a gameplay input/mouse-capture handoff, not a menu transition.
  await page.keyboard.press("Escape");
  await expect(transition).toHaveCount(0);
  await expect(page.locator(".pause-menu")).toHaveCount(0);
});

test("controls inherits the pause transition and returns to pause", async ({ page }) => {
  await page.goto(pauseFlow);
  const transition = page.locator(".screen-transition");
  await page.locator(".pause-menu__actions > button").nth(1).click();
  await expect(transition).toHaveAttribute("aria-busy", "true");
  await expect(page.locator(".pause-menu")).toBeVisible();
  await expect(page.locator(".settings-workspace")).toBeVisible();
  await expect(transition).toHaveAttribute("aria-busy", "false");

  await page.keyboard.press("Escape");
  await expect(page.locator(".pause-menu")).toBeVisible();
  await expect(transition).toHaveAttribute("aria-busy", "false");
});
