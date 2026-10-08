import type { Meta, StoryObj } from "@storybook/react-vite";
import { InventoryPanelHeader } from "./InventoryPanelHeader";

const meta = {
  title: "Molecules/InventoryPanelHeader",
  component: InventoryPanelHeader,
  args: {
    title: "Inventory", search: "", searchLabel: "Search inventory",
    sortLabel: "Sort backpack", onSearchChange: () => undefined, onSort: () => undefined,
  },
} satisfies Meta<typeof InventoryPanelHeader>;

export default meta;
type Story = StoryObj<typeof meta>;
export const Default: Story = {};
export const Filtering: Story = { args: { search: "stone" } };
export const ReadOnly: Story = { args: {
  onSearchChange: undefined, onSort: undefined,
} };
