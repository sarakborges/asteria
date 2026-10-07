import type { Meta, StoryObj } from "@storybook/react-vite";
import { Slider } from "./Slider";

const meta = {
  title: "Atoms/Slider",
  component: Slider,
  args: {
    value: 8,
    min: 2,
    max: 16,
    step: 1,
    ariaLabel: "Render distance",
  },
} satisfies Meta<typeof Slider>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
