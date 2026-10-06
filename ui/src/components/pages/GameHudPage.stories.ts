import type { Meta, StoryObj } from "@storybook/html-vite";
import { createGameHudPage, type GameHudPageProps } from "./GameHudPage";

const meta = {
  title: "Pages/GameHudPage",
  parameters: {
    layout: "fullscreen",
  },
  render: (args) => createGameHudPage(args).element,
  args: {
    embedded: true,
  },
} satisfies Meta<GameHudPageProps>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Connecting: Story = {};

export const Standalone: Story = {
  args: {
    embedded: false,
  },
};

export const Ready: Story = {
  render: (args) => {
    const view = createGameHudPage(args);
    view.statusCard.bridgeStatus.setLabel("bridge connected");
    view.statusCard.bridgeStatus.setTone("connected");
    view.statusCard.worldStatus.textContent =
      "chunk generated + collision ready";
    view.statusCard.playerStatus.textContent = "FPS controller ready";
    return view.element;
  },
};
