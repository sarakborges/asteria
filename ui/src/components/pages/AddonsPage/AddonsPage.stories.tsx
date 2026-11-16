import type { Meta, StoryObj } from "@storybook/react-vite";
import { AddonsPage } from "./AddonsPage";

const meta = {
  title: "Pages/AddonsPage",
  component: AddonsPage,
  parameters: { layout: "fullscreen" },
  args: {
    tab: "packs",
    state: { packs: [], mods: [], packRestartRequired: false,
      modRestartRequired: false, error: null },
    onBack: () => undefined,
    onPackScope: () => undefined,
    onMovePack: () => undefined,
    onModConfigure: () => undefined,
  },
} satisfies Meta<typeof AddonsPage>;

export default meta;
type Story = StoryObj<typeof meta>;
export const EmptyPacks: Story = {};
export const SelectedPack: Story = {
  args: {
    state: {
      packs: [{ name: "forest", id: "example:forest", title: "Forest",
        version: "1.0.0", description: "Alternate forest content",
        enabled: true, scope: "Both", order: 0, error: null }],
      mods: [], packRestartRequired: true, modRestartRequired: false, error: null,
    },
  },
};
export const InvalidProjectPack: Story = {
  args: {
    state: {
      packs: [{ name: "my_local_pack", id: "", title: "my_local_pack",
        version: "", description: "", enabled: false, scope: "Both",
        order: -1, error: "Missing pack.json manifest." }],
      mods: [], packRestartRequired: false, modRestartRequired: false,
      error: null,
    },
  },
};

export const Mods: Story = {
  args: {
    tab: "mods",
    state: {
      packs: [], packRestartRequired: false, modRestartRequired: false, error: null,
      mods: [{ id: "example:mod", name: "Example", version: "1.0",
        enabled: false, status: "disabled", settings: "{}", error: null }],
    },
  },
};
