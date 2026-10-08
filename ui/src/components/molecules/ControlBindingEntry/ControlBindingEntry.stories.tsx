import type { Meta, StoryObj } from "@storybook/react-vite";
import { ControlBindingEntry } from "./ControlBindingEntry";

const meta = {
  title: "Molecules/ControlBindingEntry",
  component: ControlBindingEntry,
  args: {
    entry: { key: "Space", action: "Jump / Ascend", bindAction: "Jump" },
    onCapture: () => undefined,
    onCancelCapture: () => undefined,
  },
} satisfies Meta<typeof ControlBindingEntry>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Editable: Story = {};
export const Capturing: Story = { args: { capturingAction: "Jump" } };
export const Fixed: Story = {
  args: { entry: { key: "WASD", action: "Movement" } },
};
