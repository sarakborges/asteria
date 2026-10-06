import type { Meta, StoryObj } from "@storybook/html-vite";
import { createInteractionPrompt } from "./InteractionPrompt";

const meta = {
  title: "Molecules/InteractionPrompt",
  render: () => {
    const view = createInteractionPrompt();
    view.setPrompt({
      key: "E",
      text: "Open storage box",
    });
    return view.element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
