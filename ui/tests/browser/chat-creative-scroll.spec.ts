import { expect, test } from "@playwright/test";

const chatStory = (name: string) =>
  `/iframe.html?id=organisms-chatpanel--${name}&viewMode=story`;
const creativeStory = "/iframe.html?id=organisms-creativeinventorypanel--scroll-memory&viewMode=story";

test("chat history uses the shared measured scrollbar only when needed", async ({ page }) => {
  await page.goto(chatStory("open"));
  const history = page.locator(".chat-panel__history");
  await expect(history.locator(".ui-scroll-area__track")).toHaveCount(0);
  await expect(history.locator('[role="log"] .chat-panel__message')).toHaveCount(2);

  await page.goto(chatStory("long-history"));
  await expect(page.locator(".chat-panel__history .ui-scroll-area__track")).toBeVisible();
  const viewport = page.locator(".chat-panel__history .ui-scroll-area__viewport");
  await expect.poll(() => viewport.evaluate(element =>
    element.scrollHeight - element.clientHeight)).toBeGreaterThan(200);
  await expect(page.locator('[role="log"] .chat-panel__message')).toHaveCount(64);
});

test("chat follows new messages only while already at the bottom", async ({ page }) => {
  await page.goto(chatStory("growing-history-scroll"));
  const viewport = page.locator(".chat-panel__history .ui-scroll-area__viewport");
  const append = page.getByRole("button", { name: "Append chat message" });

  await expect.poll(() => viewport.evaluate(element =>
    element.scrollHeight - element.clientHeight - element.scrollTop)).toBeLessThan(3);
  await viewport.evaluate(element => { element.scrollTop = 0; });
  await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBe(0);
  await append.click();
  await expect(page.locator('[role="log"] .chat-panel__message').last()).toContainText("64");
  await expect.poll(() => viewport.evaluate(element => element.scrollTop)).toBe(0);

  await viewport.evaluate(element => {
    element.scrollTop = element.scrollHeight;
  });
  await expect.poll(() => viewport.evaluate(element =>
    element.scrollHeight - element.clientHeight - element.scrollTop)).toBeLessThan(3);
  await append.click();
  await expect(page.locator('[role="log"] .chat-panel__message').last()).toContainText("65");
  await expect.poll(() => viewport.evaluate(element =>
    element.scrollHeight - element.clientHeight - element.scrollTop)).toBeLessThan(3);
});

test("Creative category and catalog share measured scrollbars and preserve memory", async ({ page }) => {
  await page.setViewportSize({ width: 1200, height: 800 });
  await page.goto(creativeStory);
  const category = page.locator(".creative-inventory-panel__category-list");
  const catalog = page.locator(".creative-inventory-panel__grid");
  await expect(page.locator(".creative-inventory-panel__category-scroll .ui-scroll-area__track")).toBeVisible();
  await expect(page.locator(".creative-inventory-panel__grid-scroll .ui-scroll-area__track")).toBeVisible();

  await catalog.focus();
  await page.keyboard.press("PageDown");
  await expect.poll(() => catalog.evaluate(element => element.scrollTop)).toBeGreaterThan(50);
  const savedCatalog = await catalog.evaluate(element => element.scrollTop);
  await category.getByRole("button", { name: "Category 16" }).click();
  await expect.poll(() => catalog.evaluate(element => element.scrollTop)).toBe(0);
  // Switching under an ongoing native PageDown must not leak the old
  // scroll animation into the selected category.
  await page.keyboard.press("PageDown");
  await category.getByRole("button", { name: "Category 15" }).click();
  await expect.poll(() => catalog.evaluate(element => element.scrollTop)).toBe(0);

  await category.getByRole("button", { name: "Everything" }).click();
  await expect.poll(() => catalog.evaluate(element => element.scrollTop)).toBeGreaterThanOrEqual(savedCatalog - 1);

  await category.evaluate(element => { element.scrollTop = 200; });
  await expect.poll(() => category.evaluate(element => element.scrollTop)).toBeGreaterThan(100);
  const savedCategory = await category.evaluate(element => element.scrollTop);

  await page.getByRole("button", { name: "Toggle Creative panel" }).click();
  await expect(page.locator(".creative-inventory-panel")).toHaveCount(0);
  await page.getByRole("button", { name: "Toggle Creative panel" }).click();
  await expect(page.locator(".creative-inventory-panel")).toBeVisible();
  await expect.poll(() => page.locator(".creative-inventory-panel__grid")
    .evaluate(element => element.scrollTop)).toBeGreaterThanOrEqual(savedCatalog - 1);
  await expect.poll(() => page.locator(".creative-inventory-panel__category-list")
    .evaluate(element => element.scrollTop)).toBeGreaterThanOrEqual(savedCategory - 1);
});

test("Creative search keeps separate scroll position for the unfiltered catalog", async ({ page }) => {
  await page.goto(creativeStory);
  const catalog = page.locator(".creative-inventory-panel__grid");
  await catalog.evaluate(element => { element.scrollTop = 220; });
  await expect.poll(() => catalog.evaluate(element => element.scrollTop)).toBeGreaterThan(100);

  const search = page.getByRole("textbox", { name: "Search creative items" });
  await search.fill("Catalog 1");
  await expect.poll(() => catalog.evaluate(element => element.scrollTop)).toBe(0);
  await search.fill("");
  await expect.poll(() => catalog.evaluate(element => element.scrollTop)).toBeGreaterThan(100);
});
