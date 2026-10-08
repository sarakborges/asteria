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
      name: "New World", mode: "Survival", ticksPerSecond: "40",
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
      name: "New World", mode: "Survival", ticksPerSecond: "40",
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
      name: "New World", mode: "Survival", ticksPerSecond: "40",
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
      ticksPerSecond: "40",
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
      ticksPerSecond: "40",
      pending: false,
      generating: false,
      errorKey: null,
    },
  },
};
