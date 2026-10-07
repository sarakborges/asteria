export type ItemStackView = {
  id: string;
  name?: string;
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
};

export type CreativeInventoryView = {
  searchQuery: string;
  selectedCategoryId: string | null;
  categories: readonly CreativeCategoryView[];
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
