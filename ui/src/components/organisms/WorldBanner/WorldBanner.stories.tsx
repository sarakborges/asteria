import type { Meta, StoryObj } from "@storybook/react-vite";
import { WorldBanner } from "./WorldBanner";

const meta = {
  title: "Organisms/WorldBanner",
  component: WorldBanner,
  args: {
    state: {
      sphere: "asteria:overworld",
      biome: "asteria:overworld/plains",
      x: 148,
      y: 93,
      z: -72,
      heading: 37.5,
    },
  },
} satisfies Meta<typeof WorldBanner>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Overworld: Story = {};
