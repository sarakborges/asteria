import { useState } from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { Button } from "../../atoms/Button/Button";
import { ScreenShell } from "../ScreenShell/ScreenShell";
import { ScreenTransition } from "./ScreenTransition";

function Preview() {
  const [page, setPage] = useState("starting");
  return (
    <ScreenTransition screenKey={page}>
      <ScreenShell title={page === "starting" ? "Starting Screen" : "World Selection"}
        footer={<Button label="Switch screen" onClick={() =>
          setPage(page === "starting" ? "worlds" : "starting")} />}>
        <div style={{ padding: 24 }}>{page === "starting"
          ? "Create a new world" : "Select a world"}</div>
      </ScreenShell>
    </ScreenTransition>
  );
}

const meta = {
  title: "Templates/ScreenTransition",
  component: ScreenTransition,
  parameters: { layout: "fullscreen" },
  render: () => <Preview />,
} satisfies Meta<typeof ScreenTransition>;

export default meta;
type Story = StoryObj<typeof meta>;

export const PreWorldNavigation: Story = {};
