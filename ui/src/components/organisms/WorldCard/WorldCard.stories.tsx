import type { Meta, StoryObj } from "@storybook/react-vite";
import { WorldCard } from "./WorldCard";

const meta = {
  title: "Organisms/WorldCard",
  component: WorldCard,
  args: {
    world: {
      id: "Asteria",
      lastSavedUnixMs: Date.UTC(2026, 9, 22, 19, 42),
      seed: "181960897289965",
      daysPassed: "3",
      sphere: "Overworld",
      coordinates: "148, 93, -72",
      compatible: true,
    },
  },
} satisfies Meta<typeof WorldCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Compatible: Story = { args: {
  onLoad: () => undefined,
  onRecreate: () => undefined,
  onDelete: () => undefined,
} };

export const WithThumbnail: Story = {
  args: {
    world: {
      id: "Saved Landscape",
      lastSavedUnixMs: Date.UTC(2026, 9, 11, 23, 0),
      seed: "12345",
      daysPassed: "4",
      sphere: "asteria:overworld",
      coordinates: "X: 4 · Z: 12 · Y: 72",
      compatible: true,
      thumbnailUrl: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+/tf8AAAAASUVORK5CYII=",
    },
    onLoad: () => undefined,
    onRecreate: () => undefined,
    onDelete: () => undefined,
  },
};

export const MetadataOnlyCandidate: Story = {
  args: {
    world: {
      id: "Metadata World",
      lastSavedUnixMs: Date.UTC(2026, 9, 8, 15, 0),
      seed: "181960897289965",
      daysPassed: "3",
      sphere: "asteria:overworld",
      coordinates: "X: 2 · Z: -4 · Y: 70",
      compatible: false,
    },
  },
};

export const Incompatible: Story = {
  args: {
    onDelete: () => undefined,
    world: {
      id: "Legacy World",
      lastSavedUnixMs: null,
      seed: "",
      daysPassed: "",
      sphere: "",
      coordinates: "",
      compatible: false,
    },
  },
};
