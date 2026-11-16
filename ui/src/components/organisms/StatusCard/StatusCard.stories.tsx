import type { Meta, StoryObj } from "@storybook/react-vite";
import { StatusCard } from "./StatusCard";

const baseState = {
  bridgeLabel: "debug.bridge.connecting",
  bridgeTone: "connecting" as const,
  worldStatus: "debug.world.waiting",
  playerStatus: "debug.player.waiting",
  lastMessage: null,
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
      bridgeLabel: "debug.bridge.browser",
      bridgeTone: "neutral",
    },
  },
};

export const Ready: Story = {
  args: {
    state: {
      bridgeLabel: "debug.bridge.connected",
      bridgeTone: "connected",
      worldStatus: "debug.world.ready",
      playerStatus: "debug.player.ready",
      lastMessage: "godot → webui: game.player_ready",
    },
  },
};
