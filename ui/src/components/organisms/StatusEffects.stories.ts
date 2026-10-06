import type { Meta, StoryObj } from "@storybook/html-vite";
import { createStatusEffects } from "./StatusEffects";

const meta = {
  title: "Organisms/StatusEffects",
  render: () => {
    const view = createStatusEffects();
    view.setEffects([
      {
        id: "haste",
        label: "Haste",
        duration: "01:42",
        tone: "positive",
      },
      {
        id: "poison",
        label: "Poison",
        duration: "00:18",
        tone: "negative",
      },
    ]);
    return view.element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
