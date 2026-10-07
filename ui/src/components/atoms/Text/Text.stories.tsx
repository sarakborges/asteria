import type { Meta, StoryObj } from "@storybook/react-vite";
import { Text } from "./Text";

const meta = {
  title: "Atoms/Text",
  component: Text,
  args: {
    text: "Asteria",
    variant: "body",
  },
  argTypes: {
    variant: {
      control: "select",
      options: [
        "eyebrow",
        "detail",
        "caption",
        "body",
        "title",
        "setting-title",
        "heading",
        "screen-title",
      ],
    },
  },
} satisfies Meta<typeof Text>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Body: Story = {};
export const Caption: Story = {
  args: {
    text: "Secondary information",
    variant: "caption",
  },
};
export const SettingTitle: Story = {
  args: {
    text: "World Seed",
    variant: "setting-title",
  },
};
export const Heading: Story = {
  args: {
    text: "World Settings",
    variant: "heading",
  },
};
export const ScreenTitle: Story = {
  args: {
    text: "Create World",
    variant: "screen-title",
  },
};
