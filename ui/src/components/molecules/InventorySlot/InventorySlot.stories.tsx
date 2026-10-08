import type { Meta, StoryObj } from "@storybook/react-vite";
import { InventorySlot } from "./InventorySlot";

const meta = {
  title: "Molecules/InventorySlot",
  component: InventorySlot,
  args: {
    item: {
      id: "asteria:stone",
      name: "Stone",
      quantity: 64,
    },
  },
} satisfies Meta<typeof InventorySlot>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Filled: Story = {};
export const Selected: Story = {
  args: {
    selected: true,
  },
};
export const Empty: Story = {
  args: {
    item: null,
  },
};

export const Metadata: Story = {
  args: {
    item: {
      id: "asteria:iron_bucket",
      name: "Iron Bucket",
      quantity: 1,
      metadata: { fluid_id: "asteria:water" },
    },
  },
};

export const ReadOnly: Story = { args: { disabled: true } };
