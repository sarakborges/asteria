import type { Meta, StoryObj } from "@storybook/react-vite";
import { ControlsPage } from "./ControlsPage";

const meta = {
  title: "Pages/ControlsPage",
  component: ControlsPage,
  parameters: { layout: "fullscreen" },
  args: {
    groups: [
      {
        title: "Movement",
        entries: [
          { key: "WASD", action: "Movement" },
          { key: "Space", action: "Jump / Ascend", bindAction: "Jump" },
          { key: "Space ×2", action: "Toggle Flight (When Available)" },
          { key: "ShiftLeft", action: "Descend", bindAction: "Descend" },
          { key: "MOUSE", action: "Look Around" },
          { key: "F5", action: "Change Perspective", bindAction: "ChangePerspective" },
        ],
      },
      {
        title: "Interface",
        entries: [
          { key: "1–9", action: "Select Hotbar Item" },
          { key: "KeyE", action: "Inventory", bindAction: "Inventory" },
          { key: "KeyT", action: "Chat", bindAction: "Chat" },
          { key: "ESC", action: "Pause / Close Modal" },
        ],
      },
      {
        title: "Actions",
        entries: [
          { key: "LMB", action: "Attack / Break / Special Actions" },
          { key: "RMB", action: "Place / Special Actions" },
          { key: "KeyR", action: "Rotate / Tool Action", bindAction: "ToolAction" },
          { key: "KeyQ", action: "Drop Item", bindAction: "DropItem" },
        ],
      },
    ],
    onBack: () => undefined,
    onCapture: () => undefined,
    onCancelCapture: () => undefined,
  },
} satisfies Meta<typeof ControlsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const FullAsteriaControls: Story = {};
export const Capturing: Story = { args: { capturingAction: "Jump" } };
export const CaptureError: Story = { args: { captureError: "That key cannot be bound." } };
