import type { Meta, StoryObj } from "@storybook/react-vite";
import { DeathScreenPage } from "./DeathScreenPage";

const meta = {
  title: "Pages/DeathScreenPage",
  component: DeathScreenPage,
  parameters: { layout: "fullscreen" },
  args: {
    keepInventory: true,
    droppedStacks: 0,
    dropCapacityExceeded: false,
    outcomeKnown: true,
    onRespawn: () => {},
    onSaveAndLeave: () => {},
  },
} satisfies Meta<typeof DeathScreenPage>;

export default meta;
type Story = StoryObj<typeof meta>;
export const InventoryKept: Story = {};
export const ItemsDropped: Story = {
  args: { keepInventory: false, droppedStacks: 12 },
};
export const CapacityProtected: Story = {
  args: { keepInventory: true, dropCapacityExceeded: true },
};

export const RestoredDeadSave: Story = {
  args: { keepInventory: false, outcomeKnown: false },
};
export const ExitSaveFailed: Story = {
  args: { saveError: true },
};
