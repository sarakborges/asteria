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
    details: [], activeLayerId: "asteria:moss",
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
