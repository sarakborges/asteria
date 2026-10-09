import type { Meta, StoryObj } from "@storybook/react-vite";
import { ItemGlyph } from "./ItemGlyph";

const meta = {
  title: "Atoms/ItemGlyph",
  component: ItemGlyph,
  args: {
    item: {
      id: "asteria:grass_block",
      name: "Grass Block",
    },
    size: "slot",
  },
} satisfies Meta<typeof ItemGlyph>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Slot: Story = {};
export const Result: Story = {
  args: {
    size: "result",
  },
};

const pixel = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=";

export const AuthoredBlockPreview: Story = {
  args: {
    item: {
      id: "asteria:stone",
      blockPreview: { kind: "cube", front: pixel, top: pixel, right: pixel },
    },
    size: "result",
  },
};

export const GroundObjectPreview: Story = {
  args: {
    item: {
      id: "asteria:pebble",
      blockPreview: { kind: "sprite", front: pixel, top: null, right: null },
    },
  },
};
