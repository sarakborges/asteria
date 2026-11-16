import type { Meta, StoryObj } from "@storybook/react-vite";
import { SaveFailureExitDialog } from "./SaveFailureExitDialog";

const meta = {
  title: "Organisms/SaveFailureExitDialog",
  component: SaveFailureExitDialog,
  parameters: { layout: "fullscreen" },
  args: {
    onCancel: () => undefined,
    onConfirm: () => undefined,
  },
} satisfies Meta<typeof SaveFailureExitDialog>;

export default meta;
type Story = StoryObj<typeof meta>;

export const SaveFailed: Story = {};
