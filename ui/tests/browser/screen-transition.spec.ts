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
