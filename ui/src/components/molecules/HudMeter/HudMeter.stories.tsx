import type { Meta, StoryObj } from "@storybook/react-vite";
import { HudMeter } from "./HudMeter";

const meta = {
  title: "Molecules/HudMeter",
  component: HudMeter,
  args: {
    label: "Health",
    value: 0.72,
    tone: "health",
  },
} satisfies Meta<typeof HudMeter>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Health: Story = {};

export const Stamina: Story = {
  args: {
    label: "Stamina",
    value: 0.46,
    tone: "stamina",
  },
};
