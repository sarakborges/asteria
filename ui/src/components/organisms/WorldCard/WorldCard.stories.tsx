import type { Meta, StoryObj } from "@storybook/react-vite";
import { WorldCard } from "./WorldCard";

const meta = {
  title: "Organisms/WorldCard",
  component: WorldCard,
  args: {
    world: {
      id: "Asteria",
      lastSaved: "Today 19:42",
      seed: "181960897289965",
      daysPassed: "3",
      sphere: "Overworld",
      coordinates: "148, 93, -72",
      compatible: true,
    },
  },
} satisfies Meta<typeof WorldCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Compatible: Story = { args: { onLoad: () => undefined, onDelete: () => undefined } };

export const Incompatible: Story = {
  args: { onDelete: () => undefined,
  args: {
    world: {
      id: "Legacy World",
      lastSaved: "",
      seed: "",
      daysPassed: "",
      sphere: "",
      coordinates: "",
      compatible: false,
    },
  },
};
