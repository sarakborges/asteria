import type { Meta, StoryObj } from "@storybook/react-vite";
import { StorageBoxPage } from "./StorageBoxPage";

const storage = Array.from({ length: 27 }, (_, index) =>
  index === 0
    ? { id: "asteria:stone", kind: "block" as const, quantity: 64, metadata: {} }
    : index === 1
      ? { id: "asteria:oak_log", kind: "block" as const, quantity: 18, metadata: {} }
      : null,
);
const inventory = {
  open: false, creativeAvailable: false, selectedIndex: 0,
  backpack: Array.from({ length: 27 }, (_, index) => index === 0
    ? { id: "asteria:dirt", kind: "block" as const, quantity: 32, metadata: {} } : null),
  hotbar: Array.from({ length: 9 }, (_, index) => index === 0
    ? { id: "asteria:grass_block", kind: "block" as const, quantity: 12, metadata: {} } : null),
  cursor: null,
  catalog: [],
  errorKey: null,
};

const meta = {
  title: "Pages/StorageBoxPage",
  component: StorageBoxPage,
  parameters: { layout: "fullscreen" },
  args: {
    storage: { open: true, position: { x: 3, y: 5, z: 3 }, slots: storage, errorKey: null },
    inventory,
    onClose: () => undefined,
    onSortStorage: () => undefined,
    onSortInventory: () => undefined,
    onStorageSlotClick: () => undefined,
    onInventorySlotClick: () => undefined,
  },
} satisfies Meta<typeof StorageBoxPage>;

export default meta;
type Story = StoryObj<typeof meta>;
export const Default: Story = {};
export const Cursor: Story = {
  args: { inventory: { ...inventory, cursor: {
    id: "asteria:stone", kind: "block", quantity: 3, metadata: {},
  } } },
};
export const FullCursor: Story = {
  args: { storage: { ...meta.args.storage, errorKey: "inventory.error.full" },
    inventory: { ...inventory, cursor: {
      id: "asteria:stone", kind: "block", quantity: 64, metadata: {},
    } } },
};
export const Empty: Story = {
  args: { storage: { ...meta.args.storage, slots: Array(27).fill(null) },
    inventory: { ...inventory, backpack: Array(27).fill(null), hotbar: Array(9).fill(null) } },
};
