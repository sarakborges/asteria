import type { Meta, StoryObj } from "@storybook/html-vite";
import { createCrosshair } from "./Crosshair";

const meta = {
  title: "Atoms/Crosshair",
  render: () => {
    const stage = document.createElement("div");
    stage.style.position = "relative";
    stage.style.width = "180px";
    stage.style.height = "180px";
    stage.style.background = "#14191e";
    stage.append(createCrosshair());
    return stage;
  },
} satisfies Meta;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};
