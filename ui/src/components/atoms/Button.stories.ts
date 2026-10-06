import type { Meta, StoryObj } from "@storybook/html-vite";
import { createButton, type ButtonProps } from "./Button";

const meta = {
  title: "Atoms/Button",
  render: (args) => createButton(args),
  args: {
    label: "Ping Godot",
    disabled: false,
  },
} satisfies Meta<ButtonProps>;

export default meta;
type Story = StoryObj<ButtonProps>;

export const Default: Story = {};

export const Disabled: Story = {
  args: {
    disabled: true,
  },
};
