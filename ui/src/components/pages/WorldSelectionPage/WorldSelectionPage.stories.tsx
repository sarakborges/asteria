import type { Meta, StoryObj } from "@storybook/react-vite";
import { WorldSelectionPage } from "./WorldSelectionPage";

const meta = {
  title: "Pages/WorldSelectionPage",
  component: WorldSelectionPage,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    worlds: [
      {
        id: "Asteria",
        lastSaved: "Today 19:42",
        seed: "181960897289965",
        daysPassed: "3",
        sphere: "Overworld",
        coordinates: "148, 93, -72",
        compatible: true,
      },
      {
        id: "Umbral Test",
        lastSaved: "Yesterday 23:11",
        seed: "9201882",
        daysPassed: "1",
        sphere: "Umbral",
        coordinates: "12, 88, 40",
        compatible: true,
      },
    ],
    onBack: () => undefined,
    onCreateWorld: () => undefined,
  },
} satisfies Meta<typeof WorldSelectionPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const WithWorlds: Story = {};

export const Empty: Story = {
  args: {
    worlds: [],
    status: "No restorable worlds.",
  },
};
