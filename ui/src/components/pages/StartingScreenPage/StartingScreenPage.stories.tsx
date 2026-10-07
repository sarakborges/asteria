import type { Meta, StoryObj } from "@storybook/react-vite";
import { StartingScreenPage } from "./StartingScreenPage";

const meta = {
  title: "Pages/StartingScreenPage",
  component: StartingScreenPage,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    settingsAvailable: false,
    controlsAvailable: false,
    onPlay: () => undefined,
    onExit: () => undefined,
  },
} satisfies Meta<typeof StartingScreenPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const CurrentAsteria: Story = {};

export const FullMineCloneMenu: Story = {
  args: {
    settingsAvailable: true,
    controlsAvailable: true,
    onSettings: () => undefined,
    onControls: () => undefined,
  },
};
