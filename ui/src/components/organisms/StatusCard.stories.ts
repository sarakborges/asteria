import type { Meta, StoryObj } from "@storybook/html-vite";
import { createStatusCard, type StatusCardProps } from "./StatusCard";

const meta = {
  title: "Organisms/StatusCard",
  render: (args) => createStatusCard(args).element,
  args: {
    embedded: true,
  },
} satisfies Meta<StatusCardProps>;

export default meta;
type Story = StoryObj<StatusCardProps>;

export const Embedded: Story = {};

export const Standalone: Story = {
  args: {
    embedded: false,
  },
};

export const Ready: Story = {
  render: (args) => {
    const view = createStatusCard(args);
    view.bridgeStatus.setLabel("bridge connected");
    view.bridgeStatus.setTone("connected");
    view.worldStatus.textContent = "chunk generated + collision ready";
    view.playerStatus.textContent = "FPS controller ready";
    view.lastMessage.textContent = "godot → webui: game.player_ready";
    return view.element;
  },
};
