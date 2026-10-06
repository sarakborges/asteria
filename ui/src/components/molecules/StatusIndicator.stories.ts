import type { Meta, StoryObj } from "@storybook/html-vite";
import {
  createStatusIndicator,
  type StatusIndicatorProps,
} from "./StatusIndicator";

const meta = {
  title: "Molecules/StatusIndicator",
  render: (args) => createStatusIndicator(args).element,
  args: {
    label: "bridge connected",
    tone: "connected",
  },
  argTypes: {
    tone: {
      control: "select",
      options: ["neutral", "connecting", "connected"],
    },
  },
} satisfies Meta<StatusIndicatorProps>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Connected: Story = {};

export const Connecting: Story = {
  args: {
    label: "connecting to Godot",
    tone: "connecting",
  },
};

export const Neutral: Story = {
  args: {
    label: "standalone browser mode",
    tone: "neutral",
  },
};
