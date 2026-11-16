import { useState } from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { SpawnBiomeSelect } from "./SpawnBiomeSelect";
function Preview({ required = false }: { required?: boolean }) {
  const [value, setValue] = useState<string | null>(null);
  return <div style={{ width: 420, padding: 12 }}>
    <SpawnBiomeSelect value={value} onChange={setValue}
      options={[
        { id: "asteria:overworld/plains", label: "Plains" },
        { id: "asteria:overworld/swamp", label: "Swamp" },
        { id: "asteria:overworld/mountains", label: "Mountains" },
      ]}
      label="Spawn Biome" randomLabel="Random"
      searchPlaceholder="Search biomes..."
      requireSelection={required} />
  </div>;
}
const meta = { title: "Molecules/SpawnBiomeSelect",
  component: SpawnBiomeSelect,
  render: () => <Preview /> } satisfies Meta<typeof SpawnBiomeSelect>;
export default meta;
type Story = StoryObj<typeof meta>;
export const RandomSpawn: Story = {};
export const SingleBiomeRequired: Story = { render: () => <Preview required /> };
