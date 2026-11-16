import type { Meta, StoryObj } from "@storybook/react-vite";
import { ToastStack } from "./ToastStack";

const meta = {
  title: "Organisms/ToastStack",
  component: ToastStack,
  args: {
    toasts: [
      {
        id: 1,
        message: "World ready",
        tone: "success",
        durationMs: 0,
      },
      {
        id: 2,
        message: "Inventory is not available yet",
        tone: "info",
        durationMs: 0,
      },
    ],
    onDismiss: () => undefined,
  },
} satisfies Meta<typeof ToastStack>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
