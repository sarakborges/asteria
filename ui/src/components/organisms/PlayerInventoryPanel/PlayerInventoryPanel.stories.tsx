import type { Meta, StoryObj } from "@storybook/react-vite";
import { PlayerInventoryPanel } from "./PlayerInventoryPanel";

const meta = {
  title: "Organisms/PlayerInventoryPanel",
  component: PlayerInventoryPanel,
  args: {
    state: {
      searchQuery: "",
      backpack: [
        {
          id: "asteria:stone",
          name: "Stone",
          quantity: 64,
        },
        {
          id: "asteria:dirt",
          name: "Dirt",
          quantity: 32,
        },
      ],
      hotbar: [
        {
          id: "asteria:grass_block",
          name: "Grass Block",
          quantity: 12,
        },
      ],
    },
  },
} satisfies Meta<typeof PlayerInventoryPanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
