import type { Meta, StoryObj } from "@storybook/react-vite";
import { Crosshair } from "../../atoms/Crosshair/Crosshair";
import { FpsCounter } from "../../atoms/FpsCounter/FpsCounter";
import { InteractionPrompt } from "../../molecules/InteractionPrompt/InteractionPrompt";
import { Hotbar } from "../../organisms/Hotbar/Hotbar";
import { HudEntityCard } from "../../organisms/HudEntityCard/HudEntityCard";
import { StatusCard } from "../../organisms/StatusCard/StatusCard";
import { StatusEffects } from "../../organisms/StatusEffects/StatusEffects";
import { TargetHud } from "../../organisms/TargetHud/TargetHud";
import { ToastStack } from "../../organisms/ToastStack/ToastStack";
import { WorldBanner } from "../../organisms/WorldBanner/WorldBanner";
import { WorldClock } from "../../organisms/WorldClock/WorldClock";
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
    targetOverlay: (
      <TargetHud
        state={{
          kind: "block",
          id: "asteria:stone",
          name: "Stone",
          details: [
            "Sky Light: 12 | Block Light: 0",
            "12, 88, 40",
          ],
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
    worldClock: (
      <WorldClock
        state={{
          day: 3,
          hour: 17,
          minute: 42,
        }}
      />
    ),
    hotbar: (
      <Hotbar
        state={{
          selectedIndex: 0,
          selectedName: "Stone",
          slots: [
            {
              id: "asteria:stone",
              quantity: 64,
            },
          ],
        }}
      />
    ),
    playerHud: (
      <HudEntityCard
        entity={{
          name: "Player",
          health: {
            current: 86,
            maximum: 100,
          },
        }}
      />
    ),
    statusEffects: (
      <StatusEffects
        effects={[]}
      />
    ),
    toastStack: (
      <ToastStack
        toasts={[]}
        onDismiss={() => undefined}
      />
    ),
    fpsCounter: (
      <FpsCounter fps={144} />
    ),
    debugOverlay: (
      <StatusCard
        embedded
        state={{
          bridgeLabel:
            "connecting to Godot",
          bridgeTone:
            "connecting",
          worldStatus:
            "waiting for chunk",
          playerStatus:
            "waiting for player",
          lastMessage:
            "no bridge messages yet",
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
