import type { Meta, StoryObj } from "@storybook/react-vite";
import { StatusEffects } from "./StatusEffects";

const meta = {
  title: "Organisms/StatusEffects",
  component: StatusEffects,
  args: {
    effects: [
      {
        id: "haste",
        label: "Haste",
        duration: "01:42",
        tone: "positive",
      },
      {
        id: "poison",
        label: "Poison",
        duration: "00:18",
        tone: "negative",
      },
    ],
  },
} satisfies Meta<typeof StatusEffects>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
