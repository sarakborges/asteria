import type { Meta, StoryObj } from "@storybook/react-vite";
import { Toggle } from "../../atoms/Toggle/Toggle";
import { SettingRow } from "./SettingRow";

const meta = {
  title: "Molecules/SettingRow",
  component: SettingRow,
  args: {
    title: "Show coordinates",
    description: "Display player coordinates in the HUD.",
    control: (
      <Toggle
        checked
        ariaLabel="Show coordinates"
      />
    ),
  },
} satisfies Meta<typeof SettingRow>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
