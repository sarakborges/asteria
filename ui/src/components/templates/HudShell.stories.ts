import type { Meta, StoryObj } from "@storybook/html-vite";
import { createCrosshair } from "../atoms/Crosshair";
import { createInteractionPrompt } from "../molecules/InteractionPrompt";
import { createHotbar } from "../organisms/Hotbar";
import { createWorldBanner } from "../organisms/WorldBanner";
import { createPlayerVitals } from "../organisms/PlayerVitals";
import { createStatusCard } from "../organisms/StatusCard";
import { createStatusEffects } from "../organisms/StatusEffects";
import { createToastStack } from "../organisms/ToastStack";
import { createHudShell } from "./HudShell";

const meta = {
  title: "Templates/HudShell",
  parameters: {
    layout: "fullscreen",
  },
  render: () => {
    const hotbar = createHotbar();
    hotbar.setState({
      selectedIndex: 0,
      slots: [{ id: "asteria:stone", quantity: 64 }],
    });

    const worldBanner = createWorldBanner();
    worldBanner.setState({
      sphere: "asteria:overworld",
      x: 148,
      y: 93,
      z: -72,
      heading: 37.5,
    });

    const prompt = createInteractionPrompt();
    prompt.setPrompt({
      key: "E",
      text: "Interact",
    });

    return createHudShell({
      crosshair: createCrosshair(),
      interactionPrompt: prompt.element,
      worldBanner: worldBanner.element,
      hotbar: hotbar.element,
      playerVitals: createPlayerVitals().element,
      statusEffects: createStatusEffects().element,
      toastStack: createToastStack().element,
      debugOverlay: createStatusCard({ embedded: true }).element,
    }).element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
