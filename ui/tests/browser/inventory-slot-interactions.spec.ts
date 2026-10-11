import { expect, test } from "@playwright/test";

const url = "/iframe.html?id=molecules-inventoryslot--interactions&viewMode=story";

test("inventory slot distinguishes left, shift and right clicks", async ({ page }) => {
  await page.goto(url);
  const slot = page.getByRole("button", { name: "Stone" });
  const result = page.getByTestId("inventory-slot-action");

  await slot.click();
  await expect(result).toHaveText("left");

  await slot.click({ modifiers: ["Shift"] });
  await expect(result).toHaveText("quick");

  await slot.click({ button: "right" });
  await expect(result).toHaveText("right");
});
