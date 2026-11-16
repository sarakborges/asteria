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
type Story = StoryObj<typeof PlayerVitals>;

export const Default: Story = {};

export const SurvivalHurt: Story = {
  args: {
    state: {
      health: { current: 7, maximum: 20 },
      stamina: null,
    },
  },
};

export const Dead: Story = {
  args: {
    state: {
      health: { current: 0, maximum: 20 },
      stamina: null,
    },
  },
};

export const Respawned: Story = {
  args: {
    state: {
      health: { current: 20, maximum: 20 },
      stamina: null,
    },
  },
};

export const Underwater: Story = {
  args: {
    state: {
      health: { current: 20, maximum: 20 },
      stamina: null,
      oxygen: { current: 6, maximum: 15 },
    },
  },
};

export const Drowning: Story = {
  args: {
    state: {
      health: { current: 14, maximum: 20 },
      stamina: null,
      oxygen: { current: 0, maximum: 15 },
    },
  },
};
