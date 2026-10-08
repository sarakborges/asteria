import type { Meta, StoryObj } from "@storybook/react-vite";
import { NumericInput } from "./NumericInput";

const meta = {
  title: "Atoms/NumericInput",
  component: NumericInput,
  args: {
    value: "40",
    min: 1,
    max: 4294967295,
    ariaLabel: "Ticks per second",
    onChange: () => undefined,
    onCommit: () => undefined,
  },
} satisfies Meta<typeof NumericInput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
export const Editing: Story = { args: { value: "" } };
export const Maximum: Story = { args: { value: "4294967295" } };
export const Disabled: Story = { args: { disabled: true } };
