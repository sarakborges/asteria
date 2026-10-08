import type { Meta, StoryObj } from "@storybook/react-vite";
import { SettingsWorkspacePage } from "./SettingsWorkspacePage";

const meta = {
  title: "Pages/SettingsWorkspacePage",
  component: SettingsWorkspacePage,
  parameters: { layout: "fullscreen" },
  args: {
    scope: "game",
    settings: {
      client: {
        renderDistanceChunks: 8,
        hud: {
          hideHints: false,
          targetBlockPosition: "Center",
          hints: { rotateBlock: true, breakOrPlaceBlock: true },
        },
        keybinds: { jump: "Space", descend: "ShiftLeft", toolAction: "KeyR",
          inventory: "KeyE", chat: "KeyT", dropItem: "KeyQ", changePerspective: "F5" },
      },
      world: { name: "New World", mode: "Survival", ticksPerSecond: 40, spawnCreatures: true },
      captureAction: null,
      errorKey: null,
    },
    onBack: () => {},
    onRenderDistance: () => {},
    onTargetPosition: () => {},
    onHideHints: () => {},
    onGameplayHint: () => {},
    onWorldTicks: () => {},
    onSpawnCreatures: () => {},
    onGameMode: () => {},
  },
} satisfies Meta<typeof SettingsWorkspacePage>;
export default meta;
type Story = StoryObj<typeof meta>;
export const Game: Story = {};
export const World: Story = { args: { scope: "world" } };
