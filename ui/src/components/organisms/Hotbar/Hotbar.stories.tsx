import type { Meta, StoryObj } from "@storybook/react-vite";
import { Hotbar } from "./Hotbar";

const meta = {
  title: "Organisms/Hotbar",
  component: Hotbar,
  args: {
    state: {
      selectedIndex: 2,
      selectedName: null,
      slots: [
        { id: "asteria:stone", quantity: 64 },
        { id: "asteria:dirt", quantity: 32 },
        { id: "asteria:grass_block", quantity: 12 },
        { id: "asteria:log_oak", quantity: 6 },
      ],
    },
  },
} satisfies Meta<typeof Hotbar>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
