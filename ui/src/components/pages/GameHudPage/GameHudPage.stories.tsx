import type { Meta, StoryObj } from "@storybook/react-vite";
import { createInitialUiState } from "../../../state/uiState";
import { GameHudPage } from "./GameHudPage";

const initial =
  createInitialUiState(true).hud;

const runtimeState = {
  ...initial,
  hotbar: {
    selectedIndex: 1,
    selectedName: null,
    slots: [
      { id: "asteria:stone", quantity: 64 },
      { id: "asteria:grass_block", quantity: 32 },
      { id: "asteria:log_oak", quantity: 12 },
    ],
  },
  world: {
    sphere: "asteria:overworld",
    biome: "asteria:overworld/plains",
    x: 148,
    y: 93,
    z: -72,
    heading: 37.5,
  },
  vitals: {
    health: { current: 86, maximum: 100 },
    stamina: { current: 63, maximum: 100 },
  },
  effects: [
    {
      id: "haste",
      label: "Haste",
      duration: "01:12",
      tone: "positive" as const,
    },
  ],
  prompt: {
    key: "E",
    text: "Open storage",
  },
  toasts: [
    {
      id: 1,
      message: "World ready",
      tone: "success" as const,
      durationMs: 0,
    },
  ],
};

const meta = {
  title: "Pages/GameHudPage",
  component: GameHudPage,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    embedded: true,
    state: runtimeState,
    onPing: () => undefined,
    onDismissToast: () => undefined,
  },
} satisfies Meta<typeof GameHudPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Runtime: Story = {};

export const DebugOverlay: Story = {
  args: {
    state: {
      ...runtimeState,
      debugVisible: true,
    },
  },
};
