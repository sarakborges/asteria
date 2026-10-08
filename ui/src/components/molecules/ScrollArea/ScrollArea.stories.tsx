import type { Meta, StoryObj } from "@storybook/react-vite";
import { ScrollArea } from "./ScrollArea";

const meta = {
  title: "Molecules/ScrollArea",
  component: ScrollArea,
  args: {
    ariaLabel: "Scrollable items",
    children: <div style={{ display: "grid", gap: 10 }}>
      {Array.from({ length: 18 }, (_, index) =>
        <div key={index} style={{ padding: 12, background: "var(--ui-color-hud-surface)" }}>
          Item {index + 1}
        </div>)}
    </div>,
  },
  decorators: [Story => <div style={{ height: 290, width: 320 }}><Story /></div>],
} satisfies Meta<typeof ScrollArea>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Overflow: Story = {};
export const NoOverflow: Story = {
  args: { children: <div style={{ padding: 12 }}>Short content</div> },
};
