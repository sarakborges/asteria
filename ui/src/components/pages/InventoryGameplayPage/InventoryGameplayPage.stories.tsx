import type { Meta, StoryObj } from "@storybook/react-vite";
import { InventoryGameplayPage } from "./InventoryGameplayPage";
import everythingIcon from "../../../../../packs/default/resources/textures/creative_categories/everything.png?url";
import stoneIcon from "../../../../../packs/default/resources/textures/creative_categories/stone_blocks.png?url";
import toolsIcon from "../../../../../packs/default/resources/textures/creative_categories/tools.png?url";

const meta = {
  title: "Pages/InventoryGameplayPage",
  component: InventoryGameplayPage,
  parameters: { layout: "fullscreen" },
  args: {
    state: {
      open: true,
      creativeAvailable: true,
      selectedIndex: 0,
      backpack: Array.from({ length: 27 }, (_, i) =>
        i === 0 ? {
          id: "asteria:dirt", kind: "block" as const, quantity: 4, metadata: {},
        } : null),
      equipment: Array(4).fill(null),
      portraitUrl: null,
      hotbar: Array.from({ length: 9 }, (_, i) =>
        i === 0 ? {
          id: "asteria:stone", kind: "block" as const, quantity: 32, metadata: {},
        } : null),
      cursor: {
        id: "asteria:stone", kind: "block", quantity: 3, metadata: {},
      },
      categories: [
        { id: "tools", order: 70, iconUrl: toolsIcon },
        { id: "stone_blocks", order: 20, iconUrl: stoneIcon },
      ],
      everythingIconUrl: everythingIcon,
      catalog: [
        {
          id: "asteria:stone", kind: "block", name: "Stone",
          category: "stone_blocks", metadata: {},
        },
        {
          id: "asteria:dirt", kind: "block", name: "Dirt",
          category: "block/terrain", metadata: {},
        },
        {
          id: "asteria:dimensional_slicer", kind: "item",
          name: "Dimensional Slicer (Umbral)", category: "tools",
          metadata: { target_dimension: "asteria:umbral" },
        },
        {
          id: "asteria:pickaxe_rustic", kind: "tool",
          name: "Rustic Pickaxe", category: "tools", metadata: {},
        },
      ],
      recipes: [{
        id: "asteria:rustic_hatchet",
        resultId: "asteria:hatchet_rustic",
        outputQuantity: 1,
        ingredients: [
          { id: "asteria:pebble", required: 2, available: 2 },
          { id: "asteria:stick", required: 3, available: 3 },
          { id: "asteria:plant_fiber", required: 2, available: 2 },
        ],
        craftable: true,
      }],
      craftingStatus: null,
      errorKey: null,
    },
    onClose: () => {},
    onSlotClick: () => {},
    onEquipmentClick: () => {},
    onSort: () => {},
    onDiscardCursor: () => {},
    onCreativePick: () => {},
    onCraft: () => {},
    onRotatePortrait: () => {},
  },
} satisfies Meta<typeof InventoryGameplayPage>;

export default meta;
type Story = StoryObj<typeof meta>;
export const CreativeCapable: Story = {};
export const Survival: Story = {
  args: {
    state: { ...meta.args.state,
      creativeAvailable: false,
      cursor: null,
    },
  },
};
export const FullCursor: Story = {
  args: {
    state: {
      ...meta.args.state,
      errorKey: "inventory.error.full",
    },
  },
};

export const CharacterVitals: Story = {
  args: {
    state: { ...meta.args.state, creativeAvailable: false, cursor: null },
    health: { current: 18, maximum: 20 },
  },
};

export const CraftedItem: Story = {
  args: {
    state: {
      ...meta.args.state,
      creativeAvailable: false,
      craftingStatus: {
        code: "Crafted", recipeId: "asteria:rustic_hatchet",
      },
    },
  },
};

export const EmptySurvival: Story = {
  args: {
    state: {
      ...meta.args.state,
      creativeAvailable: false,
      backpack: Array(27).fill(null),
      hotbar: Array(9).fill(null),
      cursor: null,
    },
  },
};

export const AuthoredCreativeCategories: Story = {
  args: {
    state: {
      ...meta.args.state,
      cursor: null,
      catalog: [
        ...meta.args.state.catalog,
        ...Array.from({ length: 100 }, (_, index) => ({
          id: "asteria:stone_" + index,
          kind: "block" as const,
          name: "asteria:stone_" + index,
          category: "stone_blocks",
          metadata: {},
        })),
      ],
    },
  },
};
