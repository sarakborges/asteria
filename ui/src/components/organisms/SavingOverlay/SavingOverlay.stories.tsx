import type { Meta, StoryObj } from "@storybook/react-vite";
import { SavingOverlay } from "./SavingOverlay";

const meta = {
  title: "Organisms/SavingOverlay",
  component: SavingOverlay,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    visible: true,
  },
} satisfies Meta<typeof SavingOverlay>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Saving: Story = {};

export const Hidden: Story = {
  args: {
    visible: false,
  },
};
