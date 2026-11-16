import { expect, test } from "@playwright/test";
import { createHudController } from "../../src/controllers/HudController";
import { createInitialUiState } from "../../src/state/uiState";
import { createUiStore } from "../../src/state/uiStore";

const png = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGN4ZmP0HwAFtAJUX2i7uAAAAABJRU5ErkJggg==";
test("native target bridge preserves active face Layer and validates target-specific PNG", () => {
  const store = createUiStore(createInitialUiState(false));
  const controller = createHudController(store, () => {});
  const payload = {
    kind: "block", id: "asteria:stone", name: "Stone",
    skyLight: 15, blockLight: 0,
    position: { x: 3, y: 70, z: -5 },
    activeLayerId: "asteria:moss",
    activeLayerIconUrl: png,
    layers: [{ id: "asteria:moss", face: "top" }],
  };
  controller.handleGodotMessage({ type: "game.hud.target", payload });
  expect(store.getSnapshot().hud.target?.activeLayerId).toBe("asteria:moss");
  expect(store.getSnapshot().hud.target?.activeLayerIconUrl).toBe(png);
  expect(store.getSnapshot().hud.target?.layers).toEqual(payload.layers);

  controller.handleGodotMessage({ type: "game.hud.target",
    payload: { ...payload, activeLayerIconUrl: "javascript:alert(1)" } });
  expect(store.getSnapshot().hud.target?.activeLayerId).toBe("asteria:moss");
  expect(store.getSnapshot().hud.target?.activeLayerIconUrl).toBeUndefined();
  controller.handleGodotMessage({ type: "game.hud.target",
    payload: { ...payload, activeLayerId: null } });
  expect(store.getSnapshot().hud.target?.activeLayerId).toBeUndefined();
});

test("fluid target HUD shows source level, illumination and position, without a block icon", async ({ page }) => {
  await page.goto("/iframe.html?id=pages-gamehudpage--fluid-target&viewMode=story");
  const target = page.locator(".target-hud");
  await expect(target).toBeVisible();
  await expect(target.locator(".target-hud__slot")).toHaveCount(0);
  await expect(target.locator(".target-hud__copy")).toContainText("Level: 8/8");
  await expect(target.locator(".target-hud__copy")).toContainText("Sky Light: 15");
  await expect(target.locator(".target-hud__copy")).toContainText("X: 148");
});


test("target fields remain typed and reject malformed native detail values", () => {
  const store = createUiStore(createInitialUiState(false));
  const controller = createHudController(store, () => {});
  controller.handleGodotMessage({ type: "game.hud.target", payload: {
    kind: "fluid", id: "asteria:water", name: "Water",
    fluidLevel: { value: 7, maximum: 8 },
    skyLight: 15, blockLight: 4, position: { x: -4, y: 72, z: 200 },
  } });
  expect(store.getSnapshot().hud.target).toMatchObject({
    fluidLevel: { value: 7, maximum: 8 }, skyLight: 15, blockLight: 4,
    position: { x: -4, y: 72, z: 200 },
  });
  controller.handleGodotMessage({ type: "game.hud.target", payload: {
    kind: "fluid", id: "asteria:water", name: "Water",
    fluidLevel: { value: 12, maximum: 8 }, skyLight: 27,
    blockLight: -1, position: { x: 0.25, y: -9, z: 10 },
  } });
  expect(store.getSnapshot().hud.target).toMatchObject({
    kind: "fluid", id: "asteria:water",
  });
  const invalid = store.getSnapshot().hud.target;
  expect(invalid?.fluidLevel).toBeUndefined();
  expect(invalid?.skyLight).toBeUndefined();
  expect(invalid?.blockLight).toBeUndefined();
  expect(invalid?.position).toBeUndefined();
});

for (const { language, fluidLabel, lightLabel, faceLabel } of [
  { language: "portuguese_brazil", fluidLabel: "Nível: 8/8",
    lightLabel: "Luz do Céu: 15", faceLabel: "Face: Superior" },
  { language: "spanish", fluidLabel: "Nivel: 8/8",
    lightLabel: "Luz del Cielo: 15", faceLabel: "Cara: Superior" },
]) {
  test(`target HUD uses native values but localized labels in ${language}`, async ({ page }) => {
    await page.addInitScript(lang =>
      window.localStorage.setItem("asteria.language", lang), language);
    await page.goto("/iframe.html?id=pages-gamehudpage--fluid-target&viewMode=story");
    const details = page.locator(".target-hud__copy");
    await expect(details).toContainText(fluidLabel);
    await expect(details).toContainText(lightLabel);
    await expect(details).toContainText("X: 148 | Z: -72 | Y: 92");

    await page.goto("/iframe.html?id=pages-gamehudpage--object-target&viewMode=story");
    const object = page.locator(".target-hud__copy");
    await expect(object).toContainText(faceLabel);
    await expect(object).toContainText("X: -7 | Z: 23 | Y: 75");
  });
}
