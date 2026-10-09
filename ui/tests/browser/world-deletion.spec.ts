import { expect, test } from "@playwright/test";

const selectionStory = "/iframe.html?id=pages-worldselectionpage--with-worlds&viewMode=story";
const deletingStory = "/iframe.html?id=pages-worldselectionpage--deleting&viewMode=story";

test("world deletion requires a second explicit confirmation", async ({ page }) => {
  await page.goto(selectionStory);
  const first = page.locator(".world-card").first();
  await first.getByRole("button", { name: "Delete" }).click();

  const confirm = page.getByRole("alertdialog", { name: "Delete World" });
  await expect(confirm).toBeVisible();
  await expect(confirm).toContainText('Delete "Asteria" permanently?');

  await confirm.getByRole("button", { name: "Back" }).click();
  await expect(page.getByRole("alertdialog")).toHaveCount(0);
  await expect(first).toBeVisible();

  await first.getByRole("button", { name: "Delete" }).click();
  await expect(confirm).toBeVisible();
  await confirm.getByRole("button", { name: "Delete" }).click();
  await expect(page.getByRole("alertdialog")).toHaveCount(0);
});

test("world actions are disabled during native deletion", async ({ page }) => {
  await page.goto(deletingStory);
  await expect(page.locator(".world-card")).toHaveCount(2);
  await expect(page.locator(".world-card button:enabled")).toHaveCount(0);
});
