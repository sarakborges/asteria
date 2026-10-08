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
        i === 0 ? { id: "asteria:dirt", quantity: 4 } : null),
      hotbar: Array.from({ length: 9 }, (_, i) =>
        i === 0 ? { id: "asteria:stone", quantity: 32 } : null),
      cursor: { id: "asteria:stone", quantity: 3 },
      catalog: [
        { id: "asteria:stone", name: "Stone", category: "terrain" },
        { id: "asteria:dirt", name: "Dirt", category: "terrain" },
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
