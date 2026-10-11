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
