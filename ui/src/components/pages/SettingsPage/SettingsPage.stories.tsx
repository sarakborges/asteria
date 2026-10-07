import {
  useState,
} from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { Select } from "../../atoms/Select/Select";
import { Slider } from "../../atoms/Slider/Slider";
import { Toggle } from "../../atoms/Toggle/Toggle";
import { SettingRow } from "../../molecules/SettingRow/SettingRow";
import {
  SettingsPage,
  type SettingsSectionView,
} from "./SettingsPage";

function SettingsPreview() {
  const [selected, setSelected] =
    useState("graphics");

  const sections: SettingsSectionView[] = [
    {
      id: "graphics",
      label: "Graphics",
      content: (
        <div style={{ display: "grid", gap: 18 }}>
          <SettingRow
            title="Render distance"
            description="Chunks visible around the player."
            control={
              <Slider
                value={8}
                min={2}
                max={16}
                ariaLabel="Render distance"
              />
            }
          />
          <SettingRow
            title="Quality preset"
            control={
              <Select
                value="high"
                ariaLabel="Quality preset"
                options={[
                  { value: "low", label: "Low" },
                  { value: "high", label: "High" },
                ]}
              />
            }
          />
        </div>
      ),
    },
    {
      id: "hud",
      label: "HUD",
      content: (
        <SettingRow
          title="Show coordinates"
          description="Display player coordinates."
          control={
            <Toggle
              checked
              ariaLabel="Show coordinates"
            />
          }
        />
      ),
    },
  ];

  return (
    <SettingsPage
      sections={sections}
      selectedId={selected}
      onSelect={setSelected}
      onBack={() => undefined}
    />
  );
}

const meta = {
  title: "Pages/SettingsPage",
  component: SettingsPage,
  parameters: {
    layout: "fullscreen",
  },
  render: () => <SettingsPreview />,
} satisfies Meta<typeof SettingsPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Preview: Story = {};
