import type { Meta, StoryObj } from "@storybook/html-vite";
import { createHotbarSlot } from "./HotbarSlot";

const meta = {
  title: "Molecules/HotbarSlot",
  render: () => {
    const view = createHotbarSlot(0);
    view.setState({
      id: "asteria:stone",
      quantity: 64,
    });
    view.setSelected(true);
    return view.element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Selected: Story = {};
