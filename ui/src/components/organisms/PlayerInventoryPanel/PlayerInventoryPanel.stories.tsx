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
          id: "asteria:block_grass",
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

export const LocalizedSearchFiltering: Story = {
  args: {
    state: {
      searchQuery: "water",
      backpack: [
        { id: "asteria:bucket", kind: "tool", name: "Bucket", quantity: 1,
          metadata: { contained_fluid: "asteria:water" } },
        { id: "asteria:stone", kind: "block", name: "Stone", quantity: 64 },
      ],
      hotbar: [
        { id: "asteria:bucket", kind: "tool", name: "Bucket", quantity: 1,
          metadata: { contained_fluid: "asteria:water" } },
        { id: "asteria:dirt", kind: "block", name: "Dirt", quantity: 20 },
      ],
    },
    onSlotClick: () => undefined,
  },
};
