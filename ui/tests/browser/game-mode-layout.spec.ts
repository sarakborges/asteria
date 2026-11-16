import { expect, test } from "@playwright/test";

const pages = [
  "/iframe.html?id=pages-newworldpage--ready&viewMode=story",
  "/iframe.html?id=pages-settingsworkspacepage--world&viewMode=story",
];

for (const url of pages) {
  test("game modes stretch equally in " + url, async ({ page }) => {
    await page.goto(url);
    const picker = page.locator(".game-mode-picker");
    await expect(picker).toBeVisible();
    await expect(picker.locator("button")).toHaveCount(3);
    const layout = await picker.evaluate(element => {
      const buttons = [...element.querySelectorAll("button")];
      return {
        pickerWidth: element.getBoundingClientRect().width,
        widths: buttons.map(button => button.getBoundingClientRect().width),
        direction: getComputedStyle(element.closest(".setting-row")!).flexDirection,
      };
    });
    expect(layout.direction).toBe("column");
    expect(Math.max(...layout.widths) - Math.min(...layout.widths)).toBeLessThan(1);
    expect(layout.widths[0]).toBeGreaterThan(layout.pickerWidth / 4);
  });
}

test("World Type has the same equal-width presentation as Game Mode", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-newworldpage--ready&viewMode=story");
  const game = page.locator(".game-mode-picker");
  const world = page.locator(".new-world .mode-picker").filter({
    has: page.getByRole("button", { name: "Void" }),
  });
  await expect(game).toBeVisible();
  await expect(world).toBeVisible();
  const widths = await world.locator("button").evaluateAll(buttons =>
    buttons.map(button => button.getBoundingClientRect().width));
  expect(widths).toHaveLength(3);
  expect(Math.max(...widths) - Math.min(...widths)).toBeLessThan(1);
  await world.getByRole("button", { name: "Flat" }).click();
  await expect(world.getByRole("button", { name: "Flat" }))
    .toHaveAttribute("aria-pressed", "true");
});
