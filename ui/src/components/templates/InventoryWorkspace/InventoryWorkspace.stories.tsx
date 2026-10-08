import type { Meta, StoryObj } from "@storybook/react-vite";
import { InventoryWorkspace } from "./InventoryWorkspace";

const example = (name: string) => (
  <div style={{ border: "2px solid #555", padding: 18, minHeight: 140 }}>{name}</div>
);

const meta = {
  title: "Templates/InventoryWorkspace",
  component: InventoryWorkspace,
  parameters: { layout: "fullscreen" },
  args: {
    character: example("Character Info"),
    crafting: example("Crafting"),
    inventory: example("Inventory"),
    station: example("Current Station"),
    creative: example("Creative Catalog"),
  },
} satisfies Meta<typeof InventoryWorkspace>;
export default meta;
type Story = StoryObj<typeof meta>;
export const Survival: Story = {};
export const CreativeCatalog: Story = {
  args: { creativeAvailable: true, creativeVisible: true, onViewChange: () => undefined },
};
export const CreativePersonalInventory: Story = {
  args: { creativeAvailable: true, creativeVisible: false, onViewChange: () => undefined },
};
