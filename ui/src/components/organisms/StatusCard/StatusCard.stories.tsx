import type { Meta, StoryObj } from "@storybook/react-vite";
import { StatusCard } from "./StatusCard";

const baseState = {
  bridgeLabel: "connecting to Godot",
  bridgeTone: "connecting" as const,
  worldStatus: "waiting for chunk",
  playerStatus: "waiting for player",
  lastMessage: "no bridge messages yet",
};

const meta = {
  title: "Organisms/StatusCard",
  component: StatusCard,
  args: {
    embedded: true,
    state: baseState,
    onPing: () => undefined,
  },
} satisfies Meta<typeof StatusCard>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Embedded: Story = {};

export const Standalone: Story = {
  args: {
    embedded: false,
    state: {
      ...baseState,
      bridgeLabel: "standalone browser mode",
      bridgeTone: "neutral",
    },
  },
};

export const Ready: Story = {
  args: {
    state: {
      bridgeLabel: "bridge connected",
      bridgeTone: "connected",
      worldStatus: "chunk generated + collision ready",
      playerStatus: "FPS controller ready",
      lastMessage: "godot → webui: game.player_ready",
    },
  },
};
