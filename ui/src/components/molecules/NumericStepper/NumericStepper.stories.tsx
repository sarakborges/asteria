import type { Meta, StoryObj } from "@storybook/react-vite";
import { NumericStepper } from "./NumericStepper";

const meta = {
  title: "Molecules/NumericStepper",
  component: NumericStepper,
  args: {
    value: "40",
    min: 1,
    max: 4294967295,
    ariaLabel: "Ticks per second",
    onChange: () => undefined,
    onCommit: () => undefined,
  },
} satisfies Meta<typeof NumericStepper>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Normal: Story = {};
export const Minimum: Story = { args: { value: "1" } };
export const Maximum: Story = { args: { value: "4294967295" } };
export const Editing: Story = { args: { value: "" } };
export const Disabled: Story = { args: { disabled: true } };
