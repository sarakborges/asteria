import { useState } from "react";
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
      biomes: ["asteria:overworld/plains", "asteria:overworld/desert"],
      structures: ["asteria:village", "asteria:ruins"],
      locatableStructures: ["asteria:village"],
      variations: { "asteria:ruins": ["asteria:ruins/a", "asteria:ruins/b"] },
      dimensions: ["asteria:overworld", "asteria:umbral"],
      position: { x: 12, z: -35, y: 68 },
    },
    onClose: () => undefined,
    onSubmit: () => undefined,
    onMessageAction: () => undefined,
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

function InteractiveChat() {
  const [open, setOpen] = useState(true);
  const [submitted, setSubmitted] = useState("");
  const [activated, setActivated] = useState("");
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>Open chat</button>
      <output data-testid="submitted-chat">{submitted}</output>
      <output data-testid="activated-chat-action">{activated}</output>
      <ChatDock
        open={open} visible history={[{
          id: "42", text: "Found a structure at (12, 35, 68)",
          tone: "normal", localizationKey: "chat.command.locate.found",
          parameters: { name: "Village", position: "(12, 35, 68)" },
          action: "warp",
        }, {
          id: "43", text: "Structure exported.",
          tone: "normal", localizationKey: "chat.action.structureFile",
          action: "structure_file",
        }]}
        commands={meta.args.commands}
        catalog={meta.args.catalog}
        onSubmit={text => { setSubmitted(text); setOpen(false); }}
        onClose={() => setOpen(false)}
        onMessageAction={setActivated}
      />
    </>
  );
}

export const Interactions: Story = {
  render: () => <InteractiveChat />,
};
