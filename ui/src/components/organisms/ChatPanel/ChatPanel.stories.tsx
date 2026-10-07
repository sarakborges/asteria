import type { Meta, StoryObj } from "@storybook/react-vite";
import { ChatPanel } from "./ChatPanel";

const meta = {
  title: "Organisms/ChatPanel",
  component: ChatPanel,
  args: {
    open: true,
    visible: true,
    draft: "/locate ",
    history: [
      {
        id: "1",
        text: "Welcome to Asteria.",
      },
      {
        id: "2",
        text: "Found structure at 120 80 -44 ",
        tone: "link",
        actionLabel: "[Warp to]",
      },
    ],
    suggestions: [
      {
        value: "/locate biome",
        description: "Locate the nearest biome.",
      },
      {
        value: "/locate structure",
        description: "Locate the nearest structure.",
      },
    ],
    selectedSuggestionIndex: 0,
  },
} satisfies Meta<typeof ChatPanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Open: Story = {};

export const HistoryOnly: Story = {
  args: {
    open: false,
    suggestions: [],
  },
};
