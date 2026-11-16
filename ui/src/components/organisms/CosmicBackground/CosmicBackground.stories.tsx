import type { Meta, StoryObj } from "@storybook/react-vite";
import { CosmicBackground } from "./CosmicBackground";

const meta = {
  title: "Organisms/CosmicBackground",
  component: CosmicBackground,
  parameters: {
    layout: "fullscreen",
  },
  render: () => (
    <div style={{ position: "relative", width: "100vw", height: "100vh" }}>
      <CosmicBackground />
    </div>
  ),
} satisfies Meta<typeof CosmicBackground>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
