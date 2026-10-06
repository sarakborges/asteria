import type { Meta, StoryObj } from "@storybook/html-vite";
import { createToastStack } from "./ToastStack";

const meta = {
  title: "Organisms/ToastStack",
  render: () => {
    const view = createToastStack();
    view.push({
      message: "World ready",
      tone: "success",
      durationMs: 0,
    });
    view.push({
      message: "Inventory is not available yet",
      tone: "info",
      durationMs: 0,
    });
    return view.element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
