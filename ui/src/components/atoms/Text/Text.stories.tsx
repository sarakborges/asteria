import type { Meta, StoryObj } from "@storybook/react-vite";
import { Text } from "./Text";

const meta = {
  title: "Atoms/Text",
  component: Text,
  args: {
    text: "Asteria",
    variant: "detail",
  },
  argTypes: {
    variant: {
      control: "select",
      options: [
        "eyebrow",
        "title",
        "detail",
      ],
    },
  },
} satisfies Meta<typeof Text>;

export default meta;
type Story = StoryObj<typeof meta>;

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
