import type {
  InventoryCatalogEntry, InventoryCategoryState,
} from "../state/uiState";
import { readKind, readMetadata } from "./inventoryMessageSlots";
import { readBlockPreview, readPngIconUrl } from "./blockPreviewPayload";
import { asRecord } from "./messagePayload";

const MAX_CATEGORIES = 128;
const MAX_CHOICES = 4096;
const CATEGORY_ID = /^[a-z0-9_]{1,64}$/;

export type AuthoredCreativeCatalog = {
  categories: InventoryCategoryState[];
  everythingIconUrl: string;
  items: InventoryCatalogEntry[];
};

/** Parse the immutable pack catalog as one validated bridge snapshot. */
export function readCreativeCatalog(value: unknown): AuthoredCreativeCatalog | null {
  const payload = asRecord(value);
  if (!payload || !Array.isArray(payload.categories) ||
      payload.categories.length === 0 || payload.categories.length > MAX_CATEGORIES ||
      !Array.isArray(payload.items) || payload.items.length > MAX_CHOICES ||
      !readPngIconUrl(payload.everythingIconUrl))
    return null;

  const categories: InventoryCategoryState[] = [];
  const categoryIds = new Set<string>();
  for (const raw of payload.categories) {
    const category = asRecord(raw);
    if (!category || typeof category.id !== "string" ||
        !CATEGORY_ID.test(category.id) || categoryIds.has(category.id) ||
        !Number.isInteger(category.order) ||
        (category.order as number) < 0 || (category.order as number) > 65535 ||
        !readPngIconUrl(category.iconUrl))
      return null;
    categoryIds.add(category.id);
    categories.push({
      id: category.id, order: category.order as number,
      iconUrl: category.iconUrl,
    });
  }
  categories.sort((a, b) => a.order - b.order ||
    (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));

  const items: InventoryCatalogEntry[] = [];
  for (const raw of payload.items) {
    const item = asRecord(raw);
    const kind = readKind(item?.kind);
    const metadata = readMetadata(item?.metadata);
    const blockPreview = readBlockPreview(item?.blockPreview);
    if (item?.blockPreview !== null && item?.blockPreview !== undefined &&
        !blockPreview) return null;
    if (blockPreview && kind !== "block") return null;
    if (!item || !kind || !metadata ||
        Object.keys(metadata).length > 16 ||
        Object.entries(metadata).some(([key, entry]) =>
          key.length === 0 || key.length > 128 || entry.length > 256) ||
        typeof item.id !== "string" || !item.id || item.id.length > 256 ||
        typeof item.name !== "string" || !item.name || item.name.length > 256 ||
        typeof item.category !== "string" || !categoryIds.has(item.category) ||
        (item.iconUrl !== null && item.iconUrl !== undefined && !readPngIconUrl(item.iconUrl)))
      return null;

    items.push({
      id: item.id, kind, name: item.name,
      category: item.category, metadata,
      iconUrl: typeof item.iconUrl === "string" ? item.iconUrl : undefined,
      blockPreview: blockPreview ?? undefined,
    });
  }
  return { categories, everythingIconUrl: payload.everythingIconUrl, items };
}

