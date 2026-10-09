import type { GameplayInventoryState, InventoryMetadata, InventoryCatalogEntry } from "../state/uiState";

export type ItemStackView = {
  id: string;
  name?: string;
  kind?: "block" | "item" | "tool" | "layer";
  metadata?: Record<string, string>;
  quantity?: number;
  iconUrl?: string;
};

export type CharacterEquipmentView = {
  slot: string;
  item?: ItemStackView | null;
  emptyLabel: string;
  effectLabel?: string;
};

export type CharacterInfoView = {
  name: string;
  healthCurrent: number;
  healthMaximum: number;
  equipment: readonly CharacterEquipmentView[];
};

export type PlayerInventoryView = {
  searchQuery: string;
  backpack: readonly (ItemStackView | null)[];
  hotbar: readonly (ItemStackView | null)[];
};

export type CreativeCategoryView = {
  id: string;
  label: string;
  iconUrl?: string;
};

/** Per-inventory-instance scroll memory; retained across Creative tab remounts. */
export type CreativeScrollMemory = {
  categoryOffset: number;
  catalogOffsets: Map<string, number>;
};

export type CreativeInventoryView = {
  searchQuery: string;
  selectedCategoryId: string | null;
  categories: readonly CreativeCategoryView[];
  everythingIconUrl?: string | null;
  items: readonly ItemStackView[];
};

export type CraftingIngredientView = {
  item: ItemStackView;
  required: number;
  available: number;
};

export type CraftingRecipeView = {
  id: string;
  result: ItemStackView;
  outputQuantity: number;
  ingredients: readonly CraftingIngredientView[];
  craftable: boolean;
};

export type CurrentStationView = {
  eyebrow: string;
  name: string;
  description: string;
  iconUrl?: string;
};

export function sameInventoryMetadata(a: InventoryMetadata, b: InventoryMetadata): boolean {
  const keys = Object.keys(a);
  return keys.length === Object.keys(b).length &&
    keys.every(key => a[key] === b[key]);
}

export function inventoryItemView(
  slot: GameplayInventoryState["cursor"],
  catalog: readonly InventoryCatalogEntry[],
): ItemStackView | null {
  if (!slot) return null;
  const authored = catalog.find(choice => choice.id === slot.id &&
    choice.kind === slot.kind && sameInventoryMetadata(choice.metadata, slot.metadata));
  return {
    id: slot.id, kind: slot.kind, quantity: slot.quantity,
    metadata: slot.metadata, iconUrl: authored?.iconUrl,
  };
}
