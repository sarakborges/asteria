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
