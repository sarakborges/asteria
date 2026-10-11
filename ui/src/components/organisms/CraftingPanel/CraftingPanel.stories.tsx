import type { Meta, StoryObj } from "@storybook/react-vite";
import { CraftingPanel } from "./CraftingPanel";

// The only inventory-crafting recipe authored in the MineClone reference.
const hatchet = {
  id: "asteria:rustic_hatchet",
  result: { id: "asteria:hatchet_rustic", name: "Rustic Hatchet", kind: "tool" as const },
  outputQuantity: 1,
  ingredients: [
    { item: { id: "asteria:pebble", name: "Pebble", kind: "item" as const },
      required: 2, available: 2 },
    { item: { id: "asteria:stick", name: "Stick", kind: "item" as const },
      required: 3, available: 3 },
    { item: { id: "asteria:plant_fiber", name: "Plant Fiber", kind: "item" as const },
      required: 2, available: 2 },
  ],
  craftable: true,
  unavailableReason: null,
};

const meta = {
  title: "Organisms/CraftingPanel",
  component: CraftingPanel,
  args: {
    recipes: [hatchet],
    selectedRecipeId: "asteria:rustic_hatchet",
  },
} satisfies Meta<typeof CraftingPanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Ready: Story = {};
export const MissingMaterials: Story = {
  args: {
    recipes: [{
      ...hatchet,
      craftable: false,
      unavailableReason: "MissingIngredients",
      ingredients: hatchet.ingredients.map(ingredient =>
        ingredient.item.id === "asteria:stick" ? { ...ingredient, available: 1 } : ingredient),
    }],
  },
};
export const InventoryFull: Story = {
  args: {
    recipes: [{
      ...hatchet,
      craftable: false,
      unavailableReason: "InventoryFull",
    }],
  },
};
export const Unavailable: Story = {
  args: {
    recipes: [],
    selectedRecipeId: null,
  },
};
export const ReadOnlyRecipes: Story = {
  args: { onCraft: undefined, onSelectRecipe: undefined },
};
