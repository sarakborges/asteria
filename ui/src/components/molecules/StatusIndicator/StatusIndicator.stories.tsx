import type { Meta, StoryObj } from "@storybook/react-vite";
import { StatusIndicator } from "./StatusIndicator";

const meta = {
  title: "Molecules/StatusIndicator",
  component: StatusIndicator,
  args: {
    label: "bridge connected",
    tone: "connected",
  },
  argTypes: {
    tone: {
      control: "select",
      options: [
        "neutral",
        "connecting",
        "connected",
      ],
    },
  },
} satisfies Meta<typeof StatusIndicator>;

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
