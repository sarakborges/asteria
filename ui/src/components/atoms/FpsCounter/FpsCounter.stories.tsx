import type { Meta, StoryObj } from "@storybook/react-vite";
import { FpsCounter } from "./FpsCounter";

const meta = {
  title: "Atoms/FpsCounter",
  component: FpsCounter,
  args: {
    fps: 144,
  },
} satisfies Meta<typeof FpsCounter>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
