import type { Meta, StoryObj } from "@storybook/react-vite";
import { BrushPalettePage } from "./BrushPalettePage";

const colors = [
  ["red", "#bd4242"], ["orange", "#d38a31"],
  ["yellow", "#ecda54"], ["green", "#5c9d39"],
  ["cyan", "#2acacb"], ["blue", "#424ab6"],
  ["purple", "#9955c5"], ["pink", "#da9faf"],
].map(([name, rgb]) => ({ id: `asteria:${name}`, rgb }));

const meta = {
  title: "Pages/BrushPalettePage",
  component: BrushPalettePage,
  parameters: { layout: "fullscreen" },
  args: {
    state: { selectedId: "asteria:red", colors },
    onSelect: () => {},
    onClose: () => {},
  },
} satisfies Meta<typeof BrushPalettePage>;

export default meta;
type Story = StoryObj<typeof meta>;
export const SelectedColor: Story = {};
export const ClearMode: Story = {
  args: {
    state: { selectedId: null, colors },
  },
};
