import type { Meta, StoryObj } from "@storybook/react-vite";
import { ChatDock } from "./ChatDock";

const meta = {
  title: "Organisms/ChatDock",
  component: ChatDock,
  parameters: { layout: "fullscreen" },
  args: {
    open: true,
    visible: true,
    history: [{ id: "1", text: "Player: Hello world", tone: "normal" }],
    commands: ["/help", "/position", "/time"],
    onClose: () => undefined,
    onSubmit: () => undefined,
  },
} satisfies Meta<typeof ChatDock>;

export default meta;
type Story = StoryObj<typeof meta>;
export const Open: Story = {};
export const ClosedWithHistory: Story = {
  args: { open: false },
};
export const Hidden: Story = {
  args: { open: false, visible: false },
};
