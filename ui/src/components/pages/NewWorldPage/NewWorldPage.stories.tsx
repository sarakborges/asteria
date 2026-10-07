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
      pending: false,
      generating: false,
      error: null,
    },
    onBack: () => undefined,
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
      pending: false,
      generating: false,
      error: "Seed inválida. Informe um número decimal de 64 bits.",
    },
  },
};

export const Generating: Story = {
  args: {
    state: {
      visible: true,
      seed: "123456789",
      pending: true,
      generating: true,
      error: null,
    },
  },
};
