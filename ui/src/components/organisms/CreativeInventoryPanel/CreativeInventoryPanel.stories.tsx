import type { Meta, StoryObj } from "@storybook/react-vite";
import { CreativeInventoryPanel } from "./CreativeInventoryPanel";

const items = Array.from(
  { length: 45 },
  (_, index) => ({
    id:
      "asteria:item_" +
      index,
    name:
      "Item " +
      index,
  }),
);

const meta = {
  title: "Organisms/CreativeInventoryPanel",
  component: CreativeInventoryPanel,
  args: {
    state: {
      searchQuery: "",
      selectedCategoryId: null,
      categories: [
        {
          id: "building",
          label: "Building",
        },
        {
          id: "nature",
          label: "Nature",
        },
        {
          id: "tools",
          label: "Tools",
        },
      ],
      items,
    },
  },
} satisfies Meta<typeof CreativeInventoryPanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
