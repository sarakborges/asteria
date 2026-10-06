import type { Meta, StoryObj } from "@storybook/html-vite";
import { createCompass } from "./Compass";

const meta = {
  title: "Molecules/Compass",
  render: () => {
    const compass = createCompass();
    compass.setHeading(112.5);
    return compass.element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Southeast: Story = {};
