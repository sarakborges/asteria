import type { Meta, StoryObj } from "@storybook/react-vite";
import { CurrentStationPanel } from "./CurrentStationPanel";

const meta = {
  title: "Organisms/CurrentStationPanel",
  component: CurrentStationPanel,
  args: {
    station: {
      eyebrow: "BASE STATION",
      name: "Inventory",
      description: "Personal crafting",
    },
  },
} satisfies Meta<typeof CurrentStationPanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Unavailable: Story = { args: { station: null } };
