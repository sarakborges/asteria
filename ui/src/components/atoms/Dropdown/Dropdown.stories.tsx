import { useState } from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { Dropdown } from "./Dropdown";

const meta = {
  title: "Atoms/Dropdown",
  component: Dropdown,
  args: {
    value: "medium",
    ariaLabel: "Render distance",
    options: [
      { value: "low", label: "Low" },
      { value: "medium", label: "Medium" },
      { value: "high", label: "High" },
    ],
    onChange: () => undefined,
  },
} satisfies Meta<typeof Dropdown>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
export const Disabled: Story = { args: { disabled: true } };
export const LongList: Story = {
  args: {
    options: Array.from({ length: 18 }, (_, index) => ({
      value: String(index),
      label: "Option " + (index + 1),
    })),
    value: "5",
  },
};

function EditableDropdown() {
  const [value, setValue] = useState("medium");
  return (
    <div style={{ width: 240 }}>
      <Dropdown value={value} options={[
        { value: "low", label: "Low" },
        { value: "medium", label: "Medium" },
        { value: "high", label: "High" },
      ]} ariaLabel="Live rendering quality" onChange={setValue} />
      <output data-testid="dropdown-value">{value}</output>
      <button type="button">Other focus target</button>
    </div>
  );
}

export const Interactive: Story = { render: () => <EditableDropdown /> };
