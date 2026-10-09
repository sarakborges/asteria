import type { BridgeMessage } from "../bridge/godotBridge";
import type {
  GameplayInventoryState, InventoryCatalogEntry,
  InventoryCraftingRecipe, InventoryCraftingStatus,
} from "../state/uiState";
import type { UiStore } from "../state/uiStore";
import { asRecord } from "./messagePayload";
import { readKind, readMetadata, readSlot, readSlots } from "./inventoryMessageSlots";

function readRecipes(value: unknown): InventoryCraftingRecipe[] | null {
  if (!Array.isArray(value) || value.length > 256) return null;
  const recipes: InventoryCraftingRecipe[] = [];
  const ids = new Set<string>();
  for (const raw of value) {
    const recipe = asRecord(raw);
    if (!recipe || typeof recipe.id !== "string" || !recipe.id ||
        ids.has(recipe.id) || typeof recipe.resultId !== "string" ||
        !recipe.resultId || !Number.isSafeInteger(recipe.outputQuantity) ||
        (recipe.outputQuantity as number) <= 0 ||
        typeof recipe.craftable !== "boolean" ||
        !Array.isArray(recipe.ingredients) ||
        recipe.ingredients.length === 0 || recipe.ingredients.length > 64)
      return null;
    ids.add(recipe.id);
    const ingredients: InventoryCraftingRecipe["ingredients"] = [];
    const ingredientIds = new Set<string>();
    for (const rawIngredient of recipe.ingredients) {
      const ingredient = asRecord(rawIngredient);
      if (!ingredient || typeof ingredient.id !== "string" || !ingredient.id ||
          ingredientIds.has(ingredient.id) ||
          !Number.isSafeInteger(ingredient.required) ||
          !Number.isSafeInteger(ingredient.available) ||
          (ingredient.required as number) <= 0 ||
          (ingredient.available as number) < 0)
        return null;
      ingredientIds.add(ingredient.id);
      ingredients.push({
        id: ingredient.id,
        required: ingredient.required as number,
        available: ingredient.available as number,
      });
    }
    recipes.push({
      id: recipe.id, resultId: recipe.resultId,
      outputQuantity: recipe.outputQuantity as number,
      ingredients, craftable: recipe.craftable,
    });
  }
  return recipes;
}

function readCraftingStatus(value: unknown): InventoryCraftingStatus | null {
  const payload = asRecord(value);
  if (!payload || typeof payload.recipeId !== "string") return null;
  if (payload.code !== "Crafted" &&
      payload.code !== "UnknownRecipe" &&
      payload.code !== "MissingIngredients" &&
      payload.code !== "InventoryFull") return null;
  return { code: payload.code, recipeId: payload.recipeId };
}

export function createInventoryController(
  store: UiStore,
  post: (type: string, payload?: unknown) => void,
) {
  return {
    close() { post("ui.inventory.close"); },
    rotatePortrait(deltaX: number) {
      if (!store.getSnapshot().inventory.open ||
          !Number.isFinite(deltaX) || Math.abs(deltaX) < 1) return;
      post("ui.player.portrait.rotate", {
        deltaX: Math.max(-120, Math.min(120, deltaX)),
      });
    },
    clickSlot(index: number) {
      if (!Number.isInteger(index) || index < 0 || index >= 36) return;
      post("ui.inventory.slot", { index });
    },
    clickEquipment(index: number) {
      if (!Number.isInteger(index) || index < 0 || index >= 4 ||
          !store.getSnapshot().inventory.open) return;
      post("ui.inventory.equipment", { index });
    },
    sort() { post("ui.inventory.sort"); },
    discardCursor() { post("ui.inventory.discard_cursor"); },
    craft(recipeId: string) {
      if (!recipeId || !store.getSnapshot().inventory.open) return;
      post("ui.inventory.craft", { recipeId });
    },
    pickCreative(choice: InventoryCatalogEntry) {
      if (!choice.id) return;
      post("ui.inventory.creative_pick", {
        id: choice.id, kind: choice.kind,
        ...(Object.keys(choice.metadata).length > 0
          ? { metadata: choice.metadata } : {}),
      });
    },
    handleGodotMessage(message: BridgeMessage) {
      const payload = asRecord(message.payload);
      if (message.type === "game.player_ready") {
        store.update(state => ({
          ...state, inventory: { ...state.inventory, portraitUrl: null },
        }));
      } else if (message.type === "game.inventory.state") {
        if (!payload || typeof payload.open !== "boolean" ||
            typeof payload.creativeAvailable !== "boolean" ||
            !Number.isInteger(payload.selectedIndex) ||
            (payload.selectedIndex as number) < 0 ||
            (payload.selectedIndex as number) >= 9)
          return;
        const backpack = readSlots(payload.backpack, 27);
        const hotbar = readSlots(payload.hotbar, 9);
        const equipment = readSlots(payload.equipment, 4);
        const recipes = readRecipes(payload.recipes);
        if (!backpack || !hotbar || !equipment || !recipes) return;
        const open = payload.open;
        store.update(state => {
          const inventory: GameplayInventoryState = {
            ...state.inventory,
            open,
            creativeAvailable: payload.creativeAvailable as boolean,
            selectedIndex: payload.selectedIndex as number,
            backpack, hotbar, equipment, cursor: readSlot(payload.cursor),
            portraitUrl: open ? state.inventory.portraitUrl : null,
            recipes, craftingStatus: open ? state.inventory.craftingStatus : null,
            errorKey: null,
          };
          return {
            ...state,
            inventory,
            navigation: {
              ...state.navigation,
              overlay: open ? "inventory"
                : state.navigation.overlay === "inventory" ? "none"
                : state.navigation.overlay,
            },
          };
        });
      } else if (message.type === "game.player.portrait") {
        const imageUrl = payload?.imageUrl;
        if (typeof imageUrl !== "string" ||
            imageUrl.length > 500_000 ||
            !/^data:image\/png;base64,[A-Za-z0-9+/=]+$/.test(imageUrl)) return;
        store.update(state => ({
          ...state, inventory: { ...state.inventory, portraitUrl: imageUrl },
        }));
      } else if (message.type === "game.inventory.catalog") {
        if (!payload || !Array.isArray(payload.items)) return;
        const catalog = payload.items.flatMap((raw): InventoryCatalogEntry[] => {
          const item = asRecord(raw);
          const kind = readKind(item?.kind);
          const metadata = readMetadata(item?.metadata);
          return item && kind && metadata &&
            typeof item.id === "string" &&
            typeof item.name === "string" &&
            typeof item.category === "string"
              ? [{
                id: item.id, kind, name: item.name,
                category: item.category, metadata,
                iconUrl: typeof item.iconUrl === "string" &&
                  item.iconUrl.startsWith("data:image/png;base64,")
                    ? item.iconUrl : undefined,
              }]
              : [];
        });
        store.update(state => ({
          ...state,
          inventory: { ...state.inventory, catalog },
        }));
      } else if (message.type === "game.inventory.crafting_result") {
        const craftingStatus = readCraftingStatus(message.payload);
        if (!craftingStatus) return;
        store.update(state => ({
          ...state,
          inventory: { ...state.inventory, craftingStatus },
        }));
      } else if (message.type === "game.inventory.error") {
        const errorKey = payload?.code === "InventoryFull"
          ? "inventory.error.full"
          : "inventory.error.invalid";
        store.update(state => ({
          ...state,
          inventory: { ...state.inventory, errorKey },
        }));
      }
    },
  };
}
