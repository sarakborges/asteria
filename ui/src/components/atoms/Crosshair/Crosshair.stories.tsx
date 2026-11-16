import type { Meta, StoryObj } from "@storybook/react-vite";
import { Crosshair } from "./Crosshair";

const meta = {
  title: "Atoms/Crosshair",
  component: Crosshair,
  render: () => (
    <div
      style={{
        position: "relative",
        width: 180,
        height: 180,
        background: "#14191e",
      }}
    >
      <Crosshair />
    </div>
  ),
} satisfies Meta<typeof Crosshair>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
