import type { Meta, StoryObj } from "@storybook/react-vite";
import { InteractionPrompt } from "./InteractionPrompt";

const meta = {
  title: "Molecules/InteractionPrompt",
  component: InteractionPrompt,
  args: {
    prompt: {
      key: "E",
      text: "Open storage box",
    },
  },
} satisfies Meta<typeof InteractionPrompt>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
