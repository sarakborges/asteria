import type { Meta, StoryObj } from "@storybook/react-vite";
import { PlayerVitals } from "./PlayerVitals";

const meta = {
  title: "Organisms/PlayerVitals",
  component: PlayerVitals,
  args: {
    state: {
      health: { current: 82, maximum: 100 },
      stamina: { current: 41, maximum: 100 },
    },
  },
} satisfies Meta<typeof PlayerVitals>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
