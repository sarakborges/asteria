import type { Meta, StoryObj } from "@storybook/html-vite";
import { createText, type TextProps } from "./Text";

const meta = {
  title: "Atoms/Text",
  render: (args) => createText(args),
  args: {
    text: "Asteria",
    variant: "detail",
  },
  argTypes: {
    variant: {
      control: "select",
      options: ["eyebrow", "title", "detail"],
    },
  },
} satisfies Meta<TextProps>;

export default meta;
type Story = StoryObj<TextProps>;

export const Detail: Story = {};

export const Eyebrow: Story = {
  args: {
    text: "ASTERIA / WEBUI",
    variant: "eyebrow",
  },
};

export const Title: Story = {
  args: {
    text: "WEBUI ONLINE",
    variant: "title",
  },
};
