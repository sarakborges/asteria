import type { Meta, StoryObj } from "@storybook/react-vite";
import { ControlsPage, type ControlsPageProps } from "./ControlsPage";
import { buildControlGroups } from "../../../presentation/controlGroups";
import { useLocalization } from "../../../localization/LocalizationProvider";

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
          { key: "W ×2", action: "Run (Double-Tap Forward)" },
          { key: "Space", action: "Jump / Ascend", bindAction: "Jump" },
          { key: "Space ×2", action: "Toggle Flight (When Available)" },
          { key: "ShiftLeft", action: "Descend", bindAction: "Descend" },
          { key: "MOUSE", action: "Look Around" },
        ],
      },
      {
        title: "Interface",
        entries: [
          { key: "1–9", action: "Select Hotbar Item" },
          { key: "KeyE", action: "Inventory", bindAction: "Inventory" },
          { key: "ESC", action: "Pause / Close Modal" },
          { key: "F5", action: "Change Perspective", bindAction: "ChangePerspective" },
        ],
      },
      {
        title: "Actions",
        entries: [
          { key: "LMB", action: "Attack / Break / Special Actions" },
          { key: "RMB", action: "Place / Special Actions" },
          { key: "MMB", action: "Pick Targeted Block (Creative)" },
          { key: "KeyR", action: "Rotate / Tool Action", bindAction: "ToolAction" },
          { key: "KeyQ", action: "Drop Item", bindAction: "DropItem" },
        ],
      },
      {
        title: "Chat",
        entries: [
          { key: "KeyT", action: "Chat", bindAction: "Chat" },
          { key: "ENTER", action: "Send Chat Message" },
          { key: "↑ / ↓", action: "Browse Chat History" },
          { key: "TAB", action: "Autocomplete Command" },
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

// One short section must not produce an ornamental scrollbar.
export const MinimalControls: Story = {
  args: {
    groups: [{
      title: "Movement",
      entries: [{ key: "WASD", action: "Movement" }],
    }],
  },
};
export const Capturing: Story = { args: { capturingAction: "Jump" } };
export const CaptureError: Story = { args: { captureError: "That key cannot be bound." } };

// Use the production, translated groups in this story so Chromium checks
// the real presenter rather than a second hand-written fixture.
function RuntimeControlGroups(props: ControlsPageProps) {
  const { t } = useLocalization();
  return <ControlsPage {...props} groups={buildControlGroups(t, {
    jump: "Space", descend: "ShiftLeft", inventory: "KeyE",
    chat: "KeyY", toolAction: "KeyR", dropItem: "KeyQ",
    changePerspective: "F5",
  })} />;
}

export const LocalizedChatShortcuts: Story = {
  render: args => <RuntimeControlGroups {...args} />,
};
