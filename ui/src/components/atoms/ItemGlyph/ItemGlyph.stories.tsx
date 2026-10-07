import type { Meta, StoryObj } from "@storybook/react-vite";
import { ItemGlyph } from "./ItemGlyph";

const meta = {
  title: "Atoms/ItemGlyph",
  component: ItemGlyph,
  args: {
    item: {
      id: "asteria:grass_block",
      name: "Grass Block",
    },
    size: "slot",
  },
} satisfies Meta<typeof ItemGlyph>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Slot: Story = {};
export const Result: Story = {
  args: {
    size: "result",
  },
};
