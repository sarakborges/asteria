import type { Meta, StoryObj } from "@storybook/react-vite";
import { CharacterInfoPanel } from "./CharacterInfoPanel";

const meta = {
  title: "Organisms/CharacterInfoPanel",
  component: CharacterInfoPanel,
  args: {
    state: {
      name: "Player",
      healthCurrent: 82,
      healthMaximum: 100,
      equipment: [
        {
          slot: "helm",
          emptyLabel: "No helm equipped",
        },
        {
          slot: "chest",
          emptyLabel: "No breastplate equipped",
        },
        {
          slot: "legs",
          emptyLabel: "No leggings equipped",
        },
        {
          slot: "boots",
          emptyLabel: "No boots equipped",
        },
      ],
    },
  },
} satisfies Meta<typeof CharacterInfoPanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
