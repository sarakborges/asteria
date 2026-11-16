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
        id: "asteria:block_grass",
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
    id: "asteria:block_grass",
    name: "Grass Block",
    skyLight: 15, blockLight: 0,
    position: { x: 148, y: 92, z: -72 },
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
      id: "asteria:block_grass",
      name: "Grass Block", kind: "block",
      category: "blocks_natural", metadata: {},
      blockPreview: {
        kind: "cube", top: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=",
        front: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=",
        right: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=",
      },
    }, {
      id: "asteria:moss", kind: "layer", name: "Moss",
      category: "natural_blocks", metadata: {},
      iconUrl: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAusB9T9VJ9QAAAAASUVORK5CYII=",
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

export const ActiveAttachedLayer: Story = {
  args: {
    state: {
      ...runtimeState,
      target: {
        ...runtimeState.target,
        activeLayerId: "asteria:moss",
        layers: [{ id: "asteria:moss", face: "top" as const }],
      },
    },
  },
};

export const UnavailableLayerIcon: Story = {
  args: {
    state: {
      ...runtimeState,
      target: {
        ...runtimeState.target,
        activeLayerId: "asteria:unknown_layer",
        layers: [{ id: "asteria:unknown_layer", face: "top" as const }],
      },
    },
  },
};

/** Both previews originate from native snapshots; Creative holds stale colors. */
export const NativeTintedHudIcons: Story = {
  args: {
    state: {
      ...runtimeState,
      hotbar: {
        selectedIndex: 0,
        selectedName: "Moss",
        slots: [
          {
            id: "asteria:moss", kind: "layer" as const, quantity: 2,
            iconUrl: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGPQuBTwHwAEugJKI9WgoAAAAABJRU5ErkJggg==",
          },
          {
            id: "asteria:block_grass", kind: "block" as const, quantity: 8,
            blockPreview: {
              kind: "sprite" as const, front: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGN4ZmP0HwAFtAJUX2i7uAAAAABJRU5ErkJggg==",
              top: null, right: null,
            },
          },
        ],
      },
      target: {
        ...runtimeState.target,
        activeLayerId: "asteria:moss",
        activeLayerIconUrl: "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR4nGN4ZmP0HwAFtAJUX2i7uAAAAABJRU5ErkJggg==",
        layers: [{ id: "asteria:moss", face: "top" as const }],
      },
    },
  },
};

export const FluidTarget: Story = {
  args: {
    state: {
      ...runtimeState,
      target: {
        kind: "fluid" as const,
        id: "asteria:water",
        name: "asteria:water",
        fluidLevel: { value: 8, maximum: 8 },
        skyLight: 15, blockLight: 0,
        position: { x: 148, y: 92, z: -72 },
      },
    },
  },
};

export const ObjectTarget: Story = {
  args: {
    state: {
      ...runtimeState,
      target: {
        kind: "object" as const,
        id: "asteria:flower",
        name: "asteria:flower",
        face: "top" as const,
        skyLight: 11,
        blockLight: 6,
        position: { x: -7, y: 75, z: 23 },
      },
    },
  },
};
