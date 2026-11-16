import type { Meta, StoryObj } from "@storybook/react-vite";
import { HudEntityCard } from "./HudEntityCard";

const meta = {
  title: "Organisms/HudEntityCard",
  component: HudEntityCard,
  args: {
    entity: {
      name: "Player",
      health: {
        current: 82,
        maximum: 100,
      },
    },
  },
} satisfies Meta<typeof HudEntityCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Player: Story = {};

export const WithStamina: Story = {
  args: {
    secondaryVital: {
      label: "Stamina",
      value: {
        current: 48,
        maximum: 100,
      },
    },
  },
};
