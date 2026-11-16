import { expect, test } from "@playwright/test";

const story = (name: string) =>
  `/iframe.html?id=pages-newworldpage--${name}&viewMode=story`;

test("spawn biome names track localized pack content and search accepts unaccented text", async ({ page }) => {
  await page.addInitScript(() => window.localStorage.setItem(
    "asteria.language", "portuguese_brazil"));
  await page.goto(story("single-biome"));

  await page.getByRole("button", { name: "Geração do Mundo" }).click();
  const trigger = page.getByRole("button", { name: "Bioma Inicial" });
  await expect(trigger).toContainText("Planícies");
  await trigger.click();
  const options = page.getByRole("dialog", { name: "Bioma Inicial" });
  await expect(options.getByRole("option", { name: "Aleatório" })).toHaveCount(0);
  await options.getByRole("textbox", { name: "Buscar biomas..." }).fill("pantano");
  await expect(options.getByRole("option", { name: "Pântano" })).toBeVisible();
  await options.getByRole("option", { name: "Pântano" }).click();
  await expect(trigger).toContainText("Pântano");
});

test("random initial biome remains available outside Single Biome mode", async ({ page }) => {
  await page.goto(story("ready"));
  await page.getByRole("button", { name: "World Generation" }).click();
  await page.getByRole("button", { name: "Spawn Biome" }).click();
  await expect(page.getByRole("dialog", { name: "Spawn Biome" })
    .getByRole("option", { name: "Random" })).toBeVisible();
});
