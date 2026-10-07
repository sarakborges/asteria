import type { Meta, StoryObj } from "@storybook/react-vite";
import { Surface } from "./Surface";

const meta = {
  title: "Atoms/Surface",
  component: Surface,
  args: {
    variant: "frosted",
    children: (
      <div style={{ padding: 18 }}>
        Frosted surface
      </div>
    ),
  },
  argTypes: {
    variant: {
      control: "select",
      options: ["frosted", "hud", "inset", "elevated"],
    },
  },
} satisfies Meta<typeof Surface>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Frosted: Story = {};
export const Inset: Story = {
  args: {
    variant: "inset",
  },
};
