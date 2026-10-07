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
      phaseLabel: "Materializando área inicial",
      completed: 23,
      total: 61,
      dimension: "asteria:overworld",
    },
  },
} satisfies Meta<typeof LoadingOverlay>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Materializing: Story = {};

export const RetiringDimension: Story = {
  args: {
    state: {
      phaseLabel: "Encerrando Sphere atual",
      completed: 0,
      total: 0,
      dimension: "asteria:overworld",
    },
  },
};
