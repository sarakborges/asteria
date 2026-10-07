import type { Meta, StoryObj } from "@storybook/react-vite";
import { Button } from "./Button";

const meta = {
  title: "Atoms/Button",
  component: Button,
  args: {
    label: "Continue",
    disabled: false,
    variant: "normal",
    size: "compact",
  },
  argTypes: {
    variant: {
      control: "select",
      options: ["normal", "primary", "danger"],
    },
    size: {
      control: "select",
      options: ["compact", "menu"],
    },
  },
} satisfies Meta<typeof Button>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Normal: Story = {};
export const Primary: Story = {
  args: {
    label: "Create World",
    variant: "primary",
  },
};
export const Danger: Story = {
  args: {
    label: "Delete World",
    variant: "danger",
  },
};
export const Menu: Story = {
  args: {
    label: "Play",
    variant: "primary",
    size: "menu",
  },
};
export const Disabled: Story = {
  args: {
    disabled: true,
  },
};
