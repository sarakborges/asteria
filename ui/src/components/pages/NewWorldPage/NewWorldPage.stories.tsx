import type { Meta, StoryObj } from "@storybook/react-vite";
import { NewWorldPage } from "./NewWorldPage";

const meta = {
  title: "Pages/NewWorldPage",
  component: NewWorldPage,
  parameters: {
    layout: "fullscreen",
  },
  args: {
    state: {
      visible: true,
      seed: "181960897289965",
      name: "New World", mode: "Survival", ticksPerSecond: "40", spawnCreatures: true,
      pending: false,
      generating: false,
      errorKey: null,
    },
    onBack: () => undefined,
    onMainMenu: () => undefined,
    onCreate: () => undefined,
    onRandomize: () => undefined,
  },
} satisfies Meta<typeof NewWorldPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Ready: Story = {};

export const ValidationError: Story = {
  args: {
    state: {
      visible: true,
      seed: "18446744073709551615",
      name: "New World", mode: "Survival", ticksPerSecond: "40", spawnCreatures: true,
      pending: false,
      generating: false,
      errorKey: "newWorld.error.invalidSeed",
    },
  },
};

export const Generating: Story = {
  args: {
    state: {
      visible: true,
      seed: "123456789",
      name: "New World", mode: "Survival", ticksPerSecond: "40", spawnCreatures: true,
      pending: true,
      generating: true,
      errorKey: null,
    },
  },
};

export const Pending: Story = {
  args: {
    state: {
      visible: true,
      seed: "123456789",
      name: "New World",
      mode: "Survival",
      ticksPerSecond: "40", spawnCreatures: true,
      pending: true,
      generating: false,
      errorKey: null,
    },
  },
};

export const Creative: Story = {
  args: {
    state: {
      visible: true,
      seed: "420",
      name: "Creative World",
      mode: "Creative",
      ticksPerSecond: "40", spawnCreatures: true,
      pending: false,
      generating: false,
      errorKey: null,
    },
  },
};

export const NoCreatureSpawning: Story = {
 args: { state: {
   visible: true, seed: "419", name: "Peaceful World",
   mode: "Survival", ticksPerSecond: "40", spawnCreatures: false,
   pending: false, generating: false, errorKey: null,
 } },
 play: async ({ canvasElement }) => {
   const nav = canvasElement.querySelector(".settings-page__navigation");
   const panels = canvasElement.querySelectorAll(".settings-page__section");
   if (!nav || panels.length !== 2 ||
       !canvasElement.querySelector(".new-world__form-rows--rules")) {
     throw new Error("New World must display World Settings, Game Rules and left navigation");
   }
 },
};
