import { useState } from "react";
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

function ChangingContent() {
  const [many, setMany] = useState(false);
  return (
    <ScrollArea ariaLabel="Dynamic list">
      <button type="button" onClick={() => setMany(value => !value)}>
        Toggle items
      </button>
      {Array.from({ length: many ? 24 : 1 }, (_, index) =>
        <div key={index} style={{ height: 40 }}>Row {index + 1}</div>)}
    </ScrollArea>
  );
}

export const DynamicOverflow: Story = {
  render: () => <ChangingContent />,
};
