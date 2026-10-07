import type { Meta, StoryObj } from "@storybook/react-vite";
import { CraftingPanel } from "./CraftingPanel";

const recipes = [
  {
    id: "asteria:planks",
    result: {
      id: "asteria:oak_planks",
      name: "Oak Planks",
    },
    outputQuantity: 4,
    ingredients: [
      {
        item: {
          id: "asteria:oak_log",
          name: "Oak Log",
        },
        required: 1,
        available: 6,
      },
    ],
    craftable: true,
  },
  {
    id: "asteria:workbench",
    result: {
      id: "asteria:workbench",
      name: "Workbench",
    },
    outputQuantity: 1,
    ingredients: [
      {
        item: {
          id: "asteria:oak_planks",
          name: "Oak Planks",
        },
        required: 4,
        available: 2,
      },
    ],
    craftable: false,
  },
] as const;

const meta = {
  title: "Organisms/CraftingPanel",
  component: CraftingPanel,
  args: {
    recipes,
    selectedRecipeId:
      "asteria:planks",
  },
} satisfies Meta<typeof CraftingPanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Ready: Story = {};

export const MissingMaterials: Story = {
  args: {
    selectedRecipeId:
      "asteria:workbench",
  },
};
