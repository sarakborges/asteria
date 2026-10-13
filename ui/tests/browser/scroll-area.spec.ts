import { test, expect } from "@playwright/test";

const story = (name: string) =>
  `/iframe.html?id=molecules-scrollarea--${name}&viewMode=story`;

test("shows measured scrollbar only when the viewport actually overflows", async ({ page }) => {
  await page.goto(story("no-overflow"));
  await expect(page.locator(".ui-scroll-area__track")).toHaveCount(0);

  await page.goto(story("overflow"));
  await expect(page.locator(".ui-scroll-area__track")).toBeVisible();
  const viewport = page.locator(".ui-scroll-area__viewport");
  await expect.poll(() => viewport.evaluate(
    element => element.scrollHeight > element.clientHeight + 0.5,
  )).toBe(true);
});

test("native keyboard scrolling and custom track preserve one viewport scroll position", async ({ page }) => {
  await page.goto(story("overflow"));
  const viewport = page.locator(".ui-scroll-area__viewport");
  await expect(page.locator(".ui-scroll-area__thumb")).toBeVisible();
  await viewport.focus();
  await page.keyboard.press("PageDown");
  await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBeGreaterThan(10);

  await viewport.evaluate(element => { element.scrollTop = 0; });
  const track = page.locator(".ui-scroll-area__track");
  const bounds = await track.boundingBox();
  expect(bounds).not.toBeNull();
  await page.mouse.click(bounds!.x + bounds!.width / 2, bounds!.y + bounds!.height * 0.75);
  await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBeGreaterThan(60);
});

test("reacts to content growth and contraction without stale scrollbar state", async ({ page }) => {
  await page.goto(story("dynamic-overflow"));
  const area = page.locator(".ui-scroll-area");
  await expect(area.locator(".ui-scroll-area__track")).toHaveCount(0);
  await area.getByRole("button", { name: "Toggle items" }).click();
  await expect(area.locator(".ui-scroll-area__track")).toBeVisible();
  await area.getByRole("button", { name: "Toggle items" }).click();
  await expect(area.locator(".ui-scroll-area__track")).toHaveCount(0);
});
