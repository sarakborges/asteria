import { expect, test } from "@playwright/test";

const story = (name: string) =>
  `/iframe.html?id=pages-controlspage--${name}&viewMode=story`;

test("controls only shows the measured scrollbar when its content overflows", async ({ page }) => {
  await page.setViewportSize({ width: 1050, height: 500 });
  await page.goto(story("full-asteria-controls"));
  const area = page.locator(".controls-page");
  const viewport = area.locator(".ui-scroll-area__viewport");
  await expect(area.locator(".ui-scroll-area__track")).toBeVisible();
  await expect.poll(() => viewport.evaluate(element =>
    element.scrollHeight - element.clientHeight)).toBeGreaterThan(20);

  await page.goto(story("minimal-controls"));
  await expect(page.locator(".controls-page .ui-scroll-area__track")).toHaveCount(0);
});

test("controls scroll via keyboard and mouse without moving the fixed footer", async ({ page }) => {
  await page.setViewportSize({ width: 1050, height: 500 });
  await page.goto(story("full-asteria-controls"));
  const area = page.locator(".controls-page");
  const viewport = area.locator(".ui-scroll-area__viewport");
  const back = page.locator(".controls-page__back");

  await expect(area.locator(".ui-scroll-area__thumb")).toBeVisible();
  await expect(back).toBeVisible();
  const footerY = (await back.boundingBox())?.y;
  expect(footerY).toBeDefined();

  await viewport.focus();
  await page.keyboard.press("PageDown");
  await expect.poll(() => viewport.evaluate(element =>
    element.scrollTop)).toBeGreaterThan(10);

  await viewport.evaluate(element => { element.scrollTop = 0; });
  await viewport.hover();
  await page.mouse.wheel(0, 250);
  await expect.poll(() => viewport.evaluate(element =>
    element.scrollTop)).toBeGreaterThan(10);

  await viewport.evaluate(element => { element.scrollTop = 0; });
  const track = area.locator(".ui-scroll-area__track");
  const box = await track.boundingBox();
  expect(box).not.toBeNull();
  await page.mouse.click(box!.x + box!.width / 2,
    box!.y + box!.height * 0.8);
  await expect.poll(() => viewport.evaluate(element =>
    element.scrollTop)).toBeGreaterThan(50);

  await expect(back).toBeVisible();
  expect((await back.boundingBox())?.y).toBe(footerY);
});
