import type { Meta, StoryObj } from "@storybook/react-vite";
import { Compass } from "./Compass";

const meta = {
  title: "Molecules/Compass",
  component: Compass,
  args: {
    heading: 112.5,
  },
} satisfies Meta<typeof Compass>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Southeast: Story = {};
