import type { Meta, StoryObj } from "@storybook/react-vite";
import { ItemTooltip } from "./ItemTooltip";

const meta = {
  title: "Molecules/ItemTooltip",
  component: ItemTooltip,
  args: { item: { id: "asteria:iron_bucket", name: "Iron Bucket",
    metadata: { fluid_id: "asteria:water" }, quantity: 1 },
    previewPosition: { x: 120, y: 100 } },
} satisfies Meta<typeof ItemTooltip>;
export default meta;
type Story = StoryObj<typeof meta>;
export const WithMetadata: Story = {};
export const WithoutMetadata: Story = { args: {
  item: { id: "asteria:stone", name: "Stone", quantity: 64 },
} };
