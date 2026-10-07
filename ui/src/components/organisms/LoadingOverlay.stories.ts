import type { Meta, StoryObj } from "@storybook/html-vite";
import { createLoadingOverlay } from "./LoadingOverlay";

const meta = {
  title: "Organisms/LoadingOverlay",
  parameters: {
    layout: "fullscreen",
  },
  render: () => {
    const view = createLoadingOverlay();
    view.show({
      phaseLabel: "Materializando área inicial",
      completed: 23,
      total: 61,
      dimension: "asteria:overworld",
    });
    return view.element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Materializing: Story = {};

export const RetiringDimension: Story = {
  render: () => {
    const view = createLoadingOverlay();
    view.show({
      phaseLabel: "Encerrando Sphere atual",
      completed: 0,
      total: 0,
      dimension: "asteria:overworld",
    });
    return view.element;
  },
};
