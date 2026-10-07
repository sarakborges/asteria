import type { Meta, StoryObj } from "@storybook/react-vite";
import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import { ScreenShell } from "./ScreenShell";

const meta = {
  title: "Templates/ScreenShell",
  component: ScreenShell,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    title: "Create World",
    background: <CosmicBackground />,
    children: (
      <Surface variant="frosted">
        <div style={{ padding: 18 }}>
          Screen content
        </div>
      </Surface>
    ),
    footer: (
      <Button
        label="Continue"
        variant="primary"
        size="menu"
      />
    ),
  },
} satisfies Meta<typeof ScreenShell>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
