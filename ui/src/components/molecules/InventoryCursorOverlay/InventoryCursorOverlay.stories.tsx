import type { Meta, StoryObj } from "@storybook/react-vite";
import { InventoryCursorOverlay } from "./InventoryCursorOverlay";

const meta = {
  title: "Molecules/InventoryCursorOverlay",
  component: InventoryCursorOverlay,
  args: {
    item: { id: "asteria:stone", name: "Stone", quantity: 16 },
    previewPosition: { x: 120, y: 100 },
  },
} satisfies Meta<typeof InventoryCursorOverlay>;

export default meta;
type Story = StoryObj<typeof meta>;
export const Stack: Story = {};
export const Single: Story = { args: {
  item: { id: "asteria:bucket", name: "Bucket", quantity: 1 },
} };
