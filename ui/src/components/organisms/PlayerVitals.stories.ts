import type { Meta, StoryObj } from "@storybook/html-vite";
import { createPlayerVitals } from "./PlayerVitals";

const meta = {
  title: "Organisms/PlayerVitals",
  render: () => {
    const view = createPlayerVitals();
    view.setState({
      health: { current: 82, maximum: 100 },
      stamina: { current: 41, maximum: 100 },
    });
    return view.element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
