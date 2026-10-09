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

export const TexturedBlock: Story = {
  args: {
    preview: {
      kind: "cube",
      top: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=",
      front: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=",
      right: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=",
    },
  },
};
