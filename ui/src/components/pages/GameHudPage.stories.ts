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
type Story = StoryObj<GameHudPageProps>;

export const Runtime: Story = {
  render: (args) => {
    const view = createGameHudPage(args);
    view.worldBanner.setState({
      sphere: "asteria:overworld",
      x: 148,
      y: 93,
      z: -72,
      heading: 37.5,
    });
    view.hotbar.setState({
      selectedIndex: 1,
      slots: [
        { id: "asteria:stone", quantity: 64 },
        { id: "asteria:grass_block", quantity: 32 },
        { id: "asteria:log_oak", quantity: 12 },
      ],
    });
    view.playerVitals.setState({
      health: { current: 86, maximum: 100 },
      stamina: { current: 63, maximum: 100 },
    });
    view.statusEffects.setEffects([
      { id: "haste", label: "Haste", duration: "01:12", tone: "positive" },
    ]);
    view.interactionPrompt.setPrompt({
      key: "E",
      text: "Open storage",
    });
    view.toasts.push({
      message: "World ready",
      tone: "success",
      durationMs: 0,
    });
    return view.element;
  },
};

export const DebugOverlay: Story = {
  render: (args) => {
    const view = createGameHudPage(args);
    view.shell.setDebugVisible(true);
    return view.element;
  },
};
