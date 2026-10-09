import type { Meta, StoryObj } from "@storybook/react-vite";
import { createInitialUiState } from "../../../state/uiState";
import { GameHudPage } from "./GameHudPage";

const initial =
  createInitialUiState(true).hud;

const runtimeState = {
  ...initial,
  hotbar: {
    selectedIndex: 1,
    selectedName: "Grass Block",
    slots: [
      {
        id: "asteria:stone",
        kind: "block" as const,
        quantity: 64,
      },
      {
        id: "asteria:grass_block",
        kind: "block" as const,
        quantity: 32,
      },
      {
        id: "asteria:log_oak",
        quantity: 12,
      },
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
    health: {
      current: 86,
      maximum: 100,
    },
    stamina: {
      current: 63,
      maximum: 100,
    },
  },
  target: {
    kind: "block" as const,
    id: "asteria:grass_block",
    name: "Grass Block",
    details: [
      "Sky Light: 15 | Block Light: 0",
      "148, 92, -72",
    ],
  },
  miningProgress: null,
  artisansKitResolution: null,
  targetEntity: null,
  clock: {
    day: 3,
    hour: 17,
    minute: 42,
  },
  fps: 144,
  effects: [
    {
      id: "haste",
      label: "Haste",
      duration: "01:12",
      tone: "positive" as const,
    },
  ],
  prompt: {
    key: "RMB",
    text: "Place block",
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
    catalog: [{
      id: "asteria:grass_block",
      name: "Grass Block", kind: "block",
      category: "natural_blocks", metadata: {},
      blockPreview: {
        kind: "cube", top: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=",
        front: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=",
        right: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=",
      },
    }],
    onPing: () => undefined,
    onDismissToast: () => undefined,
  },
} satisfies Meta<typeof GameHudPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Runtime: Story = {};

export const CreatureTarget: Story = {
  args: {
    state: {
      ...runtimeState,
      target: null,
      targetEntity: {
        name: "Slime",
        health: {
          current: 18,
          maximum: 24,
        },
      },
    },
  },
};

export const MiningInProgress: Story = {
  args: {
    state: {
      ...runtimeState,
      miningProgress: 0.6,
    },
  },
};

export const ArtisanMicroblockResolution: Story = {
  args: {
    state: {
      ...runtimeState,
      artisansKitResolution: 2,
    },
  },
};

export const DebugOverlay: Story = {
  args: {
    state: {
      ...runtimeState,
      debugVisible: true,
    },
  },
};
