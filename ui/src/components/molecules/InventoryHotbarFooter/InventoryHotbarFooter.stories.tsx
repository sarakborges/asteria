import type { Meta, StoryObj } from "@storybook/react-vite";
import { InventoryHotbarFooter } from "./InventoryHotbarFooter";

const example = Array.from({ length: 9 }, (_, index) =>
  index === 0 ? { id: "asteria:stone", quantity: 32 } : null);

const meta = {
  title: "Molecules/InventoryHotbarFooter",
  component: InventoryHotbarFooter,
  args: { hotbar: example, onSlotClick: () => undefined, onTrash: () => undefined },
} satisfies Meta<typeof InventoryHotbarFooter>;
export default meta;
type Story = StoryObj<typeof meta>;
export const Active: Story = {};
export const NoCursorAction: Story = { args: { onTrash: undefined } };
export const ReadOnly: Story = {
  args: { onSlotClick: undefined, onTrash: undefined },
};

export const StorageHotbar: Story = {
  args: { showTrash: false, onTrash: undefined },
};
