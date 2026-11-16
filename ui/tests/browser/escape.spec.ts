import { expect, test } from "@playwright/test";

const story = (name: string) =>
  "/iframe.html?id=pages-newworldpage--" + name + "&viewMode=story";

test("Escape goes back one pre-world screen without closing a seed editor's parent", async ({ page }) => {
  await page.goto(story("real-navigation"));
  await expect(page.locator(".starting-screen")).toBeVisible();
  await page.locator(".starting-screen__button").first().click();
  await expect(page.locator(".world-selection-screen")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.locator(".starting-screen")).toBeVisible();

  await page.locator(".starting-screen__button").first().click();
  await page.locator(".world-selection__footer-button").last().click();
  await expect(page.locator(".new-world .settings-page")).toBeVisible();

  const seed = page.locator("#world-seed");
  const original = await seed.inputValue();
  await seed.fill("123");
  await seed.press("Escape");
  await expect(seed).toHaveValue(original);
  await expect(page.locator(".new-world .settings-page")).toBeVisible();

  await page.keyboard.press("Escape");
  await expect(page.locator(".world-selection-screen")).toBeVisible();
});

test("Escape closes game settings to pause and pause to gameplay", async ({ page }) => {
  await page.goto(story("modal-navigation"));
  await expect(page.locator(".pause-menu")).toBeVisible();
  await page.locator(".pause-menu__settings-row button").last().click();
  await expect(page.locator(".settings-workspace .settings-page")).toBeVisible();

  await page.keyboard.press("Escape");
  await expect(page.locator(".pause-menu")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.locator(".pause-menu")).toHaveCount(0);
});

test("Escape still closes pause after keyboard focus moves outside the app root", async ({ page }) => {
  await page.goto(story("modal-navigation"));
  await expect(page.locator(".pause-menu")).toBeVisible();
  await page.evaluate(() => {
    document.body.tabIndex = -1;
    document.body.focus();
  });
  await expect(page.locator(".app-interaction-root")).not.toBeFocused();
  await page.keyboard.press("Escape");
  await expect(page.locator(".pause-menu")).toHaveCount(0);
});

test("Escape closes inventory through the same injected semantic action as its Close button", async ({ page }) => {
  await page.goto(story("inventory-navigation"));
  await expect(page.locator(".inventory-gameplay")).toBeVisible();
  await page.keyboard.press("Escape");
  await expect(page.locator(".inventory-gameplay")).toHaveCount(0);
  await expect(page.locator("body")).toHaveAttribute("data-inventory-close-reason", "other");
});

test("Inventory shortcut closes without being forwarded to Godot by WRY", async ({ page }) => {
  await page.goto(story("inventory-navigation"));
  await expect(page.locator(".inventory-gameplay")).toBeVisible();
  // WRY's native forwarding listener is installed on document (bubble phase).
  await page.evaluate(() => document.addEventListener("keydown", event => {
    if (event.code === "KeyE") document.body.dataset.wryForwarded = "true";
  }));

  await page.keyboard.press("e");
  await expect(page.locator(".inventory-gameplay")).toHaveCount(0);
  await expect(page.locator("body")).toHaveAttribute("data-inventory-close-reason", "shortcut");
  await expect(page.locator("body")).not.toHaveAttribute("data-wry-forwarded", "true");
});

test("Inventory key closes even if WRY leaves DOM focus on body", async ({ page }) => {
  await page.goto(story("inventory-navigation"));
  await expect(page.locator(".inventory-gameplay")).toBeVisible();

  await page.evaluate(() => {
    document.body.tabIndex = -1;
    document.body.focus();
  });
  await expect(page.locator(".app-interaction-root")).not.toBeFocused();
  await page.keyboard.press("e");
  await expect(page.locator(".inventory-gameplay")).toHaveCount(0);
});

test("Inventory shortcut types in search without forwarding to Godot", async ({ page }) => {
  await page.goto(story("inventory-navigation"));
  const inventory = page.locator(".inventory-gameplay");
  const search = inventory.locator(".player-inventory-panel input");
  await page.evaluate(() => document.addEventListener("keydown", event => {
    if (event.code === "KeyE") document.body.dataset.wryForwarded = "true";
  }));

  await search.click();
  await search.press("e");
  await expect(search).toHaveValue("e");
  await expect(inventory).toBeVisible();
  await expect(page.locator("body")).not.toHaveAttribute("data-wry-forwarded", "true");

  // WRY synthesizes forwarded keys without the native Echo/repeat flag.
  // Browser key auto-repeat must not leak to that forwarding listener either.
  await search.evaluate(input => input.dispatchEvent(new KeyboardEvent(
    "keydown", { key: "e", code: "KeyE", bubbles: true, repeat: true },
  )));
  await expect(inventory).toBeVisible();
  await expect(page.locator("body")).not.toHaveAttribute("data-wry-forwarded", "true");

  await inventory.locator(".inventory-panel-header__sort").click();
  await page.keyboard.press("e");
  await expect(inventory).toHaveCount(0);
  await expect(page.locator("body")).not.toHaveAttribute("data-wry-forwarded", "true");
});

test("Escape dismisses a focused dropdown before it navigates away", async ({ page }) => {
  await page.goto(story("modal-navigation"));
  await page.locator(".pause-menu__settings-row button").last().click();
  await expect(page.locator(".settings-workspace .settings-page")).toBeVisible();

  const dropdown = page.locator(".settings-workspace .ui-dropdown__control").first();
  await dropdown.click();
  await expect(dropdown).toHaveAttribute("aria-expanded", "true");
  await page.keyboard.press("Escape");
  await expect(dropdown).toHaveAttribute("aria-expanded", "false");
  await expect(page.locator(".settings-workspace .settings-page")).toBeVisible();

  await page.keyboard.press("Escape");
  await expect(page.locator(".pause-menu")).toBeVisible();
});
