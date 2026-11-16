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
  // The strengthened handoff may refocus immediately, even before focus fires.
  await page.evaluate(() => window.dispatchEvent(new Event("focus")));
  await expect(input).toBeFocused();
});

test("chat input autofocus survives a native focus acknowledgement", async ({ page }) => {
  await page.goto("/iframe.html?id=organisms-chatpanel--focus-flow&viewMode=story");
  await page.getByRole("button", { name: "Open chat" }).click();
  const input = page.locator(".chat-panel__input input");
  // React's autoFocus property focuses on mount; React does not
  // necessarily reflect it as a persistent HTML attribute.
  await expect(input).toBeFocused();

  // The host can acknowledge its native keyboard handoff after mounting.
  await page.getByRole("button", { name: "Native WebView focus granted" }).click();
  await expect(input).toBeFocused();
});
