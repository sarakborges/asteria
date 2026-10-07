import type { Meta, StoryObj } from "@storybook/react-vite";
import { PauseMenuPage } from "./PauseMenuPage";

const meta = {
  title: "Pages/PauseMenuPage",
  component: PauseMenuPage,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    worldSettingsAvailable: true,
    gameSettingsAvailable: true,
    controlsAvailable: true,
    onResume: () => undefined,
    onWorldSettings: () => undefined,
    onGameSettings: () => undefined,
    onControls: () => undefined,
    onLeaveWorld: () => undefined,
    onExitGame: () => undefined,
  },
} satisfies Meta<typeof PauseMenuPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const SaveFailure: Story = {
  args: {
    saveFeedback:
      "Save failed. World kept open.",
  },
};
