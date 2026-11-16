import { expect, test } from "@playwright/test";

const url = "/iframe.html?id=organisms-chatdock--interactions&viewMode=story";

test("located coordinates are a clickable chat action", async ({ page }) => {
  await page.goto(url);
  await page.getByRole("button", { name: /Warp to/ }).click();
  await expect(page.getByTestId("activated-chat-action")).toHaveText("42");
  await page.getByRole("button", { name: /Open exports folder/ }).click();
  await expect(page.getByTestId("activated-chat-action")).toHaveText("43");
});

test("submitted commands can be recalled and draft restored with arrow keys", async ({ page }) => {
  await page.goto(url);
  const input = page.getByRole("textbox", { name: "Chat message" });
  await input.fill("/kill");
  await input.press("Enter");
  await expect(page.getByTestId("submitted-chat")).toHaveText("/kill");
  await page.getByRole("button", { name: "Open chat" }).click();
  await expect(input).toBeFocused();
  await input.press("ArrowUp");
  await expect(input).toHaveValue("/kill");
  await input.press("ArrowDown");
  await expect(input).toHaveValue("");
});

test("Escape first dismisses autocomplete, then closes the focused chat", async ({ page }) => {
  await page.goto(url);
  const input = page.getByRole("textbox", { name: "Chat message" });
  await input.fill("/spa");
  await expect(page.getByRole("listbox")).toBeVisible();
  await input.press("Escape");
  await expect(page.getByRole("listbox")).toHaveCount(0);
  await expect(input).toBeFocused();
  await input.press("Escape");
  await expect(input).toHaveCount(0);
  await page.getByRole("button", { name: "Open chat" }).click();
  await expect(input).toBeFocused();
});
