import type { Meta, StoryObj } from "@storybook/react-vite";
import { useState } from "react";
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
type Story = StoryObj<typeof InventorySlot>;

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

export const DamagedArmor: Story = {
  args: {
    item: {
      id: "asteria:chestplate_wayfarer",
      name: "Wayfarer Chestplate",
      quantity: 1,
      metadata: { "asteria:durability": "71" },
      durability: { current: 71, maximum: 240 },
    },
  },
};

function InteractiveSlot() {
  const [action, setAction] = useState("none");
  return (
    <div>
      <InventorySlot item={{ id: "asteria:stone", name: "Stone", quantity: 8 }}
        onClick={setAction} />
      <output data-testid="inventory-slot-action">{action}</output>
    </div>
  );
}

export const Interactions: Story = {
  render: () => <InteractiveSlot />,
};
