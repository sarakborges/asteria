import { expect, test } from "@playwright/test";

const dropdown = "/iframe.html?id=atoms-dropdown--interactive&viewMode=story";
const stepper = "/iframe.html?id=molecules-numericstepper--interactive&viewMode=story";

test("dropdown keyboard navigation commits selection, Escape cancels and blur closes", async ({ page }) => {
  await page.goto(dropdown);
  const trigger = page.getByRole("button", { name: "Live rendering quality" });
  const output = page.getByTestId("dropdown-value");

  await trigger.focus();
  await trigger.press("ArrowDown");
  await expect(trigger).toHaveAttribute("aria-expanded", "true");
  await expect(page.getByRole("option", { name: "High" })).toBeFocused();
  await page.keyboard.press("Enter");
  await expect(output).toHaveText("high");
  await expect(trigger).toHaveAttribute("aria-expanded", "false");
  await expect(trigger).toBeFocused();

  await trigger.click();
  await expect(page.getByRole("option", { name: "High" })).toBeFocused();
  await page.keyboard.press("ArrowUp");
  await expect(page.getByRole("option", { name: "Medium" })).toBeFocused();
  await page.keyboard.press("Escape");
  await expect(output).toHaveText("high");
  await expect(trigger).toHaveAttribute("aria-expanded", "false");

  await trigger.click();
  // Click an uncovered point outside the popup. A visible popup is supposed
  // to intercept pointer events over the elements beneath it.
  await page.mouse.click(6, 6);
  await expect(trigger).toHaveAttribute("aria-expanded", "false");
});

test("dropdown popup remains within the compact viewport", async ({ page }) => {
  await page.setViewportSize({ width: 380, height: 300 });
  await page.goto("/iframe.html?id=atoms-dropdown--long-list&viewMode=story");
  const trigger = page.getByRole("button", { name: "Render distance" });
  await trigger.click();
  const panel = page.getByRole("listbox", { name: "Render distance" });
  await expect(panel).toBeVisible();
  const rect = await panel.boundingBox();
  expect(rect).not.toBeNull();
  expect(rect!.x).toBeGreaterThanOrEqual(0);
  expect(rect!.y).toBeGreaterThanOrEqual(0);
  expect(rect!.x + rect!.width).toBeLessThanOrEqual(380);
  expect(rect!.y + rect!.height).toBeLessThanOrEqual(300);
});

test("numeric editor rejects out-of-range values and commits exact uint32 maximum", async ({ page }) => {
  await page.goto(stepper);
  const input = page.getByRole("textbox", { name: "Live tick rate" });
  const committed = page.getByTestId("tick-committed");
  await input.fill("4294967296");
  await input.press("Tab");
  await expect(input).toHaveValue("40");
  await expect(committed).toHaveText("40");

  await input.fill("4294967295");
  await input.press("Enter");
  await expect(input).toHaveValue("4294967295");
  await expect(committed).toHaveText("4294967295");
  await expect(page.getByRole("button", { name: "Live tick rate: +1" })).toBeDisabled();

  await input.fill("3");
  await input.press("Escape");
  await expect(input).toHaveValue("4294967295");
  await expect(committed).toHaveText("4294967295");
});

test("numeric stepper recovers empty drafts to minimum without skipping it", async ({ page }) => {
  await page.goto(stepper);
  const input = page.getByRole("textbox", { name: "Live tick rate" });
  await input.fill("");
  await expect(input).toHaveValue("");
  await page.getByRole("button", { name: "Live tick rate: +1" }).click();
  await expect(input).toHaveValue("1");
  await expect(page.getByTestId("tick-committed")).toHaveText("1");
  await page.getByRole("button", { name: "Live tick rate: +1" }).click();
  await expect(input).toHaveValue("2");
});

test("world seed keeps uint64 precision and Escape restores last valid value", async ({ page }) => {
  await page.goto("/iframe.html?id=atoms-uint64input--maximum-seed&viewMode=story");
  const input = page.getByRole("textbox", { name: "Seed" });
  await expect(input).toHaveValue("18446744073709551615");
  await input.fill("18446744073709551616");
  await expect(input).toHaveValue("18446744073709551615");
  await input.fill("123");
  await input.press("Escape");
  await expect(input).toHaveValue("18446744073709551615");
});
