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
    commands: ["/spawn", "/place", "/locate", "/warp", "/kill", "/modify"],
    catalog: {
      creatures: ["asteria:slime_aqua", "asteria:slime_pyro"],
      biomes: ["asteria:plains", "asteria:swamp"],
      structures: ["asteria:village", "asteria:ruins"],
      variations: { "asteria:ruins": ["asteria:ruins/a", "asteria:ruins/b"] },
      dimensions: ["asteria:overworld", "asteria:umbral"],
      position: { x: 12, z: -35, y: 68 },
    },
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
