import type { Meta, StoryObj } from "@storybook/react-vite";
import { ModePicker } from "./ModePicker";

const meta = {
  title: "Molecules/ModePicker",
  component: ModePicker,
  args: {
    value: "Normal",
    modes: ["Normal", "Flat", "Void"],
    label: "World Type",
    labelFor: (mode: string) => mode,
    onChange: () => undefined,
  },
} satisfies Meta<typeof ModePicker>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Normal: Story = {};
export const Flat: Story = { args: { value: "Flat" } };
export const Disabled: Story = { args: { disabled: true } };
