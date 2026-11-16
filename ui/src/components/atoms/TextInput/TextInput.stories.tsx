import type { Meta, StoryObj } from "@storybook/react-vite";
import { TextInput } from "./TextInput";

const meta = {
  title: "Atoms/TextInput",
  component: TextInput,
  args: {
    defaultValue: "181960897289965",
    placeholder: "Seed",
  },
} satisfies Meta<typeof TextInput>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
export const FocusContract: Story = {
  args: {
    autoFocus: true,
  },
};
export const Invalid: Story = {
  args: {
    invalid: true,
  },
};
