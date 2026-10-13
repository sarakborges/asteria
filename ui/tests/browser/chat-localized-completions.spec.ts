import { expect, test } from "@playwright/test";

const story = "/iframe.html?id=organisms-chatdock--open&viewMode=story";

// This story uses the real ChatDock + ChatPanel and the selected pack's
// localization catalogs. Suggestions keep the canonical command token;
// only the human-readable description follows the active client language.
for (const [language, creature, biome, sphere] of [
  ["english", "Aqua Slime", "Plains", "The Overworld"],
  ["portuguese_brazil", "Slime Aqua", "Planícies", "Mundo da Superfície"],
  ["spanish", "Slime Aqua", "Llanuras", "Mundo de la Superficie"],
] as const) {
  test("chat command completions show pack-localized content names in " + language,
    async ({ page }) => {
      await page.addInitScript(locale =>
        window.localStorage.setItem("asteria.language", locale), language);
      await page.goto(story);
      const input = page.locator(".chat-panel__input input");
      await expect(input).toBeFocused();

      await input.fill("/spawn aqua");
      const creatureOption = page.getByRole("option")
        .filter({ hasText: "asteria:slime_aqua" }).first();
      await expect(creatureOption.locator("strong")).toHaveText("asteria:slime_aqua");
      await expect(creatureOption.locator("span")).toHaveText(creature);

      await input.fill("/locate biome plains");
      const biomeOption = page.getByRole("option")
        .filter({ hasText: "asteria:overworld/plains" });
      await expect(biomeOption.locator("strong"))
        .toHaveText("asteria:overworld/plains");
      await expect(biomeOption.locator("span")).toHaveText(biome);

      await input.fill("/warp 12 35 68 asteria:overworld");
      const sphereOption = page.getByRole("option")
        .filter({ hasText: "asteria:overworld" }).first();
      await expect(sphereOption.locator("strong")).toHaveText("asteria:overworld");
      await expect(sphereOption.locator("span")).toHaveText(sphere);
    });
}
