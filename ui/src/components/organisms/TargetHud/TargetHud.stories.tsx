import type { Meta, StoryObj } from "@storybook/react-vite";
import { TargetHud } from "./TargetHud";

const meta = {
  title: "Organisms/TargetHud",
  component: TargetHud,
  args: {
    state: {
      kind: "block",
      id: "asteria:grass_block",
      name: "Grass Block",
      details: [
        "Sky Light: 15 | Block Light: 0",
        "148, 92, -72",
      ],
    },
  },
} satisfies Meta<typeof TargetHud>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Block: Story = {};
