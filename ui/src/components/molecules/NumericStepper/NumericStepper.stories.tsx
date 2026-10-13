import { useState } from "react";
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

function EditableStepper() {
  const [value, setValue] = useState("40");
  const [committed, setCommitted] = useState("40");
  return (
    <div>
      <NumericStepper value={value} min={1} max={4294967295}
        ariaLabel="Live tick rate"
        onChange={setValue}
        onCommit={value => setCommitted(String(value))} />
      <output data-testid="tick-draft">{value}</output>
      <output data-testid="tick-committed">{committed}</output>
      <button type="button">Other focus target</button>
    </div>
  );
}
export const Interactive: Story = { render: () => <EditableStepper /> };
