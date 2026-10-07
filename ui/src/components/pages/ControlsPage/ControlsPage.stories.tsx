import type { Meta, StoryObj } from "@storybook/react-vite";
import { ControlsPage } from "./ControlsPage";

const meta = {
  title: "Pages/ControlsPage",
  component: ControlsPage,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    groups: [
      {
        title: "Movement",
        entries: [
          { key: "WASD", action: "Movement" },
          { key: "W ×2", action: "Run" },
          { key: "SPACE", action: "Jump" },
          { key: "MOUSE", action: "Look" },
        ],
      },
      {
        title: "Actions",
        entries: [
          { key: "LMB", action: "Primary action" },
          { key: "RMB", action: "Secondary action" },
          { key: "1–9", action: "Hotbar" },
          { key: "ESC", action: "Pause / close" },
        ],
      },
    ],
    onBack: () => undefined,
  },
} satisfies Meta<typeof ControlsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
