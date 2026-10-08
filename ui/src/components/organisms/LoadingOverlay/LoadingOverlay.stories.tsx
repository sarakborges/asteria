import type { Meta, StoryObj } from "@storybook/react-vite";
import { LoadingOverlay } from "./LoadingOverlay";

const meta = {
  title: "Organisms/LoadingOverlay",
  component: LoadingOverlay,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    state: {
      phase: "materializing_initial_area",
      completed: 23,
      total: 61,
      dimension: "asteria:overworld",
    },
  },
} satisfies Meta<typeof LoadingOverlay>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Materializing: Story = {};
export const Preparing: Story = {
  args: {
    state: {
      phase: "preparing_world",
      completed: 0,
      total: 0,
      dimension: "asteria:overworld",
    },
  },
};
export const RetiringDimension: Story = {
  args: {
    state: {
      phase: "retiring_current_dimension",
      completed: 0,
      total: 0,
      dimension: "asteria:overworld",
    },
  },
};
