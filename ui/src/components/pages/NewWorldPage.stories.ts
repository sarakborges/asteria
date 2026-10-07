import type { Meta, StoryObj } from "@storybook/html-vite";
import { createNewWorldPage } from "./NewWorldPage";

const meta = {
  title: "Pages/NewWorldPage",
  parameters: {
    layout: "fullscreen",
  },
  render: () => {
    const view = createNewWorldPage();
    view.setSuggestedSeed("18446744073709551615");
    return view.element;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Ready: Story = {};

export const ValidationError: Story = {
  render: () => {
    const view = createNewWorldPage();
    view.setSuggestedSeed("18446744073709551615");
    view.setError("Seed inválida. Informe um número decimal de 64 bits.");
    return view.element;
  },
};

export const Generating: Story = {
  render: () => {
    const view = createNewWorldPage();
    view.setSuggestedSeed("123456789");
    view.setGenerating("123456789");
    return view.element;
  },
};
