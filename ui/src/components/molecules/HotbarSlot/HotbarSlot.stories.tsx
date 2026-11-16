import type { Meta, StoryObj } from "@storybook/react-vite";
import { HotbarSlot } from "./HotbarSlot";

const meta = {
  title: "Molecules/HotbarSlot",
  component: HotbarSlot,
  args: {
    index: 0,
    state: {
      id: "asteria:stone",
      quantity: 64,
    },
    selected: true,
  },
} satisfies Meta<typeof HotbarSlot>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Selected: Story = {};
