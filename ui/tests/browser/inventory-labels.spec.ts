import { expect, test } from "@playwright/test";
import { inventoryDisplayName, inventorySearchMatches } from "../../src/presentation/inventoryLabels";
import { findInventoryCatalogEntry } from "../../src/presentation/inventoryModels";

const contentName = (id: string) => ({
  "asteria:bucket": "Bucket",
  "asteria:water": "Water",
  "asteria:lava": "Lava",
  "asteria:dimensional_slicer": "Dimensional Slicer",
  "asteria:umbral": "Umbral",
  "asteria:stone": "Stone",
} as Record<string, string>)[id] ?? id;

test("stack labels include localized metadata without modifying item identity", () => {
  expect(inventoryDisplayName(
    "asteria:bucket", { contained_fluid: "asteria:water" }, contentName,
  )).toBe("Bucket (Water)");
  expect(inventoryDisplayName(
    "asteria:dimensional_slicer",
    { target_dimension: "asteria:umbral" }, contentName,
  )).toBe("Dimensional Slicer (Umbral)");
  expect(inventoryDisplayName("asteria:stone", {}, contentName)).toBe("Stone");
});

test("search matches localized names, IDs and metadata, including diacritics", () => {
  const item = { id: "asteria:bucket", metadata: { contained_fluid: "asteria:water" } };
  expect(inventorySearchMatches(item, "Water", contentName)).toBe(true);
  expect(inventorySearchMatches(item, "contained_fluid", contentName)).toBe(true);
  expect(inventorySearchMatches(item, "bucket", contentName)).toBe(true);
  expect(inventorySearchMatches(item, "lava", contentName)).toBe(false);
  expect(inventorySearchMatches(null, "water", contentName)).toBe(false);
  expect(inventorySearchMatches(
    { id: "asteria:stone", name: "Lâmpada" }, "lampada", contentName,
  )).toBe(true);
});

test("catalog icon and block-preview lookup requires exact kind and metadata", () => {
  const preview = { kind: "cube" as const, top: "top", front: "front", right: "right" };
  const catalog = [
    {
      id: "asteria:stone", kind: "block" as const, name: "Stone",
      category: "stone_blocks", metadata: {}, blockPreview: preview,
    },
    {
      id: "asteria:bucket", kind: "tool" as const, name: "Bucket",
      category: "tools", metadata: { contained_fluid: "asteria:water" },
      iconUrl: "water-icon",
    },
  ];
  expect(findInventoryCatalogEntry("asteria:stone", "block", {}, catalog)?.blockPreview)
    .toBe(preview);
  expect(findInventoryCatalogEntry("asteria:bucket", "tool",
    { contained_fluid: "asteria:water" }, catalog)?.iconUrl).toBe("water-icon");
  expect(findInventoryCatalogEntry("asteria:bucket", "tool", {}, catalog))
    .toBeUndefined();
});
