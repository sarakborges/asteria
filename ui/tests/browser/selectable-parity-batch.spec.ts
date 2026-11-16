import { expect, test } from "@playwright/test";

async function background(button: import("@playwright/test").Locator) {
  return button.evaluate(element => getComputedStyle(element).backgroundColor);
}

test("selected settings/category buttons retain their selection color under hover", async ({ page }) => {
  await page.goto("/iframe.html?id=atoms-button--selected&viewMode=story");
  const button = page.getByRole("button", { name: "Selected category" });
  await expect(button).toHaveAttribute("aria-pressed", "true");
  const resting = await background(button);
  await button.hover();
  expect(await background(button)).toBe(resting);
});

test("checked toggle retains selected coloring under hover", async ({ page }) => {
  await page.goto("/iframe.html?id=atoms-toggle--on&viewMode=story");
  const toggle = page.getByRole("switch", { name: "Toggle setting" });
  const resting = await background(toggle);
  await toggle.hover();
  expect(await background(toggle)).toBe(resting);
});

test("selected inventory slot remains selected on hover and keyboard focus", async ({ page }) => {
  await page.goto("/iframe.html?id=molecules-inventoryslot--selected&viewMode=story");
  const slot = page.locator(".inventory-slot--selected");
  const resting = await background(slot);
  await slot.hover();
  expect(await background(slot)).toBe(resting);
  await slot.focus();
  expect(await background(slot)).toBe(resting);
});
