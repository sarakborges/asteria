import type { Meta, StoryObj } from "@storybook/react-vite";
import { KeyCap } from "./KeyCap";

const meta = {
  title: "Atoms/KeyCap",
  component: KeyCap,
  args: {
    label: "WASD",
  },
} satisfies Meta<typeof KeyCap>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
