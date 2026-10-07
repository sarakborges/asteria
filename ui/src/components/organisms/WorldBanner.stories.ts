import type { Meta, StoryObj } from "@storybook/html-vite";
import { createWorldBanner } from "./WorldBanner";

const meta = {
  title: "Organisms/WorldBanner",
  render: () => {
    const banner = createWorldBanner();
    banner.setState({
      sphere: "asteria:overworld",
      biome: "asteria:overworld/plains",
      x: 148,
      y: 93,
      z: -72,
      heading: 37.5,
    });
    return banner.element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Overworld: Story = {};
