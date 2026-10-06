import type { Meta, StoryObj } from "@storybook/html-vite";
import { createHotbar } from "./Hotbar";

const meta = {
  title: "Organisms/Hotbar",
  render: () => {
    const view = createHotbar();
    view.setState({
      selectedIndex: 2,
      slots: [
        { id: "asteria:stone", quantity: 64 },
        { id: "asteria:dirt", quantity: 32 },
        { id: "asteria:grass_block", quantity: 12 },
        { id: "asteria:log_oak", quantity: 6 },
      ],
    });
    return view.element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
