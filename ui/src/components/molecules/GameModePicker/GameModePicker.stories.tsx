import type { Meta, StoryObj } from "@storybook/react-vite";
import { GameModePicker } from "./GameModePicker";

const meta = {
  title: "Molecules/GameModePicker",
  component: GameModePicker,
  args: {
    value: "Survival",
    onChange: () => undefined,
  },
} satisfies Meta<typeof GameModePicker>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Survival: Story = {};
export const Creative: Story = { args: { value: "Creative" } };
export const Spectator: Story = { args: { value: "Spectator" } };
export const Disabled: Story = { args: { disabled: true } };
