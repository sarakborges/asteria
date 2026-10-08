import type { Meta, StoryObj } from "@storybook/react-vite";
import { StorageBoxPage } from "./StorageBoxPage";

const meta = {
  title: "Pages/StorageBoxPage",
  component: StorageBoxPage,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    storageSearch: "",
    inventorySearch: "",
    storage: [
      {
        id: "asteria:stone",
        name: "Stone",
        quantity: 64,
      },
      {
        id: "asteria:oak_log",
        name: "Oak Log",
        quantity: 18,
      },
    ],
    backpack: [
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
} satisfies Meta<typeof StorageBoxPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const ReadOnly: Story = {
  args: {
    onStorageSearchChange: undefined,
    onInventorySearchChange: undefined,
    onSortStorage: undefined,
    onSortInventory: undefined,
    onStorageSlotClick: undefined,
    onInventorySlotClick: undefined,
  },
};

export const Filtered: Story = {
  args: {
    storageSearch: "stone",
    inventorySearch: "dirt",
    onStorageSearchChange: () => undefined,
    onInventorySearchChange: () => undefined,
    onSortStorage: () => undefined,
    onSortInventory: () => undefined,
    onStorageSlotClick: () => undefined,
    onInventorySlotClick: () => undefined,
  },
};
