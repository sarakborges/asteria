import { expect, test } from "@playwright/test";

test("chat input receives focus every time chat opens", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-chatpanel--focus-flow&viewMode=story");
  const input = page.locator(".chat-panel__input input");
  await page.getByRole("button", { name: "Open chat" }).click();
  await expect(input).toBeFocused();
  await page.getByRole("button", { name: "Close chat" }).click();
  await expect(input).toHaveCount(0);
  await page.getByRole("button", { name: "Open chat" }).click();
  await expect(input).toBeFocused();
});

test("native WebView keyboard handoff restores chat input focus", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-chatpanel--focus-flow&viewMode=story");
  const input = page.locator(".chat-panel__input input");
  await page.getByRole("button", { name: "Open chat" }).click();
  await expect(input).toBeFocused();

  // The browser controls were mounted before the native WebView got focus.
  await page.getByRole("button", { name: "Native WebView focus granted" }).click();
  await expect(input).toBeFocused();

  // Native WRY focus can also return after a window focus event.
  await input.evaluate(element => (element as HTMLInputElement).blur());
  await expect(input).not.toBeFocused();
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await expect(input).toBeFocused();
});

test("chat remains focused after delayed native keyboard handoff", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-chatpanel--focus-flow&viewMode=story");
  await page.getByRole("button", { name: "Open chat" }).click();
  const input = page.locator(".chat-panel__input input");
  await expect(input).toBeFocused();

  // A delayed native focus transfer can blur the input after it initially
  // passed the DOM focus check; WebUI must restore it without a mouse click.
  await input.evaluate(element => (element as HTMLInputElement).blur());
  await expect(input).toBeFocused();
  await page.getByRole("button", { name: "Native WebView focus granted" }).click();
  await expect(input).toBeFocused();
});
