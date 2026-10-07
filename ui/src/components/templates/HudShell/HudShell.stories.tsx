import type { Meta, StoryObj } from "@storybook/react-vite";
import { Crosshair } from "../../atoms/Crosshair/Crosshair";
import { InteractionPrompt } from "../../molecules/InteractionPrompt/InteractionPrompt";
import { Hotbar } from "../../organisms/Hotbar/Hotbar";
import { PlayerVitals } from "../../organisms/PlayerVitals/PlayerVitals";
import { StatusCard } from "../../organisms/StatusCard/StatusCard";
import { StatusEffects } from "../../organisms/StatusEffects/StatusEffects";
import { ToastStack } from "../../organisms/ToastStack/ToastStack";
import { WorldBanner } from "../../organisms/WorldBanner/WorldBanner";
import { HudShell } from "./HudShell";

const meta = {
  title: "Templates/HudShell",
  component: HudShell,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    debugVisible: false,
    crosshair: <Crosshair />,
    interactionPrompt: (
      <InteractionPrompt
        prompt={{
          key: "E",
          text: "Interact",
        }}
      />
    ),
    worldBanner: (
      <WorldBanner
        state={{
          sphere: "asteria:overworld",
          biome: "asteria:overworld/plains",
          x: 148,
          y: 93,
          z: -72,
          heading: 37.5,
        }}
      />
    ),
    hotbar: (
      <Hotbar
        state={{
          selectedIndex: 0,
          selectedName: null,
          slots: [
            { id: "asteria:stone", quantity: 64 },
          ],
        }}
      />
    ),
    playerVitals: <PlayerVitals state={null} />,
    statusEffects: <StatusEffects effects={[]} />,
    toastStack: (
      <ToastStack
        toasts={[]}
        onDismiss={() => undefined}
      />
    ),
    debugOverlay: (
      <StatusCard
        embedded
        state={{
          bridgeLabel: "connecting to Godot",
          bridgeTone: "connecting",
          worldStatus: "waiting for chunk",
          playerStatus: "waiting for player",
          lastMessage: "no bridge messages yet",
        }}
        onPing={() => undefined}
      />
    ),
  },
} satisfies Meta<typeof HudShell>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const Debug: Story = {
  args: {
    debugVisible: true,
  },
};
