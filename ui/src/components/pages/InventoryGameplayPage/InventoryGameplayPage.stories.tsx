import type { Meta, StoryObj } from "@storybook/react-vite";
import { InventoryGameplayPage } from "./InventoryGameplayPage";

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
      hotbar: Array.from({ length: 9 }, (_, i) =>
        i === 0 ? {
          id: "asteria:stone", kind: "block" as const, quantity: 32, metadata: {},
        } : null),
      cursor: {
        id: "asteria:stone", kind: "block", quantity: 3, metadata: {},
      },
      catalog: [
        {
          id: "asteria:stone", kind: "block", name: "Stone",
          category: "block/terrain", metadata: {},
        },
        {
          id: "asteria:dirt", kind: "block", name: "Dirt",
          category: "block/terrain", metadata: {},
        },
        {
          id: "asteria:dimensional_slicer", kind: "item",
          name: "Dimensional Slicer (Umbral)", category: "item/tools",
          metadata: { target_dimension: "asteria:umbral" },
        },
        {
          id: "asteria:pickaxe_rustic", kind: "tool",
          name: "Rustic Pickaxe", category: "tool/tools", metadata: {},
        },
      ],
      errorKey: null,
    },
    onClose: () => {},
    onSlotClick: () => {},
    onSort: () => {},
    onDiscardCursor: () => {},
    onCreativePick: () => {},
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
