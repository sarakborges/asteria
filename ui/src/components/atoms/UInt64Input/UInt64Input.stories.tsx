import { useState } from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import { UInt64Input } from "./UInt64Input";
function Preview({ initial }: { initial: string }) {
 const [value, setValue] = useState(initial);
 return <UInt64Input id="seed-preview" ariaLabel="Seed" value={value} onChange={setValue} />;
}
const meta = { title: "Atoms/UInt64Input", component: UInt64Input,
 render: () => <Preview initial="18446744073709551615" /> } satisfies Meta<typeof UInt64Input>;
export default meta;
type Story = StoryObj<typeof meta>;
export const MaximumSeed: Story = {};
export const RandomSeed: Story = { render: () => <Preview initial="181960897289965" /> };
export const OptionalSeed: Story = { render: () => <Preview initial="" /> };
