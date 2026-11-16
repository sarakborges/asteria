import type { Meta, StoryObj } from "@storybook/react-vite";
import { WorldClock } from "./WorldClock";

const meta = {
  title: "Organisms/WorldClock",
  component: WorldClock,
  args: {
    state: {
      day: 3,
      hour: 17,
      minute: 42,
    },
  },
} satisfies Meta<typeof WorldClock>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
