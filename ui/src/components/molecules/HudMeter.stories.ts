import type { Meta, StoryObj } from "@storybook/html-vite";
import { createHudMeter, type HudMeterProps } from "./HudMeter";

const meta = {
  title: "Molecules/HudMeter",
  render: (args) => createHudMeter(args).element,
  args: {
    label: "Health",
    value: 0.72,
    tone: "health",
  },
} satisfies Meta<HudMeterProps>;

export default meta;
type Story = StoryObj<HudMeterProps>;

export const Health: Story = {};

export const Stamina: Story = {
  args: {
    label: "Stamina",
    value: 0.46,
    tone: "stamina",
  },
};
