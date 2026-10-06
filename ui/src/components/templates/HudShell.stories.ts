import type { Meta, StoryObj } from "@storybook/html-vite";
import { createCrosshair } from "../atoms/Crosshair";
import { createStatusCard } from "../organisms/StatusCard";
import { createHudShell } from "./HudShell";

const meta = {
  title: "Templates/HudShell",
  parameters: {
    layout: "fullscreen",
  },
  render: () =>
    createHudShell({
      statusCard: createStatusCard({ embedded: true }).element,
      crosshair: createCrosshair(),
    }),
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
