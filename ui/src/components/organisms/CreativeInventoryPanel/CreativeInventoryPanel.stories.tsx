import { useRef, useState } from "react";
import type { Meta, StoryObj } from "@storybook/react-vite";
import type { CreativeScrollMemory } from "../../../presentation/inventoryModels";
import { CreativeInventoryPanel } from "./CreativeInventoryPanel";

const items = Array.from(
  { length: 45 },
  (_, index) => ({
    id:
      "asteria:item_" +
      index,
    name:
      "Item " +
      index,
  }),
);

const meta = {
  title: "Organisms/CreativeInventoryPanel",
  component: CreativeInventoryPanel,
  args: {
    state: {
      searchQuery: "",
      selectedCategoryId: null,
      categories: [
        {
          id: "building",
          label: "Building",
        },
        {
          id: "nature",
          label: "Nature",
        },
        {
          id: "tools",
          label: "Tools",
        },
      ],
      items,
    },
  },
} satisfies Meta<typeof CreativeInventoryPanel>;

export default meta;
type Story = StoryObj<typeof meta>;

export const Default: Story = {};

export const WithHotbar: Story = {
  args: {
    hotbar: Array.from({ length: 9 }, (_, index) =>
      index === 0 ? { id: "asteria:stone", quantity: 16 } : null),
    onHotbarSlotClick: () => undefined,
    onTrash: () => undefined,
    onItemClick: () => undefined,
    onCategoryChange: () => undefined,
  },
};

/** Test the real scroll-memory owner across category/search filtering and remount. */
function ScrollMemoryFlow() {
  const [selectedCategoryId, setSelectedCategoryId] = useState<string | null>(null);
  const [searchQuery, setSearchQuery] = useState("");
  const [mounted, setMounted] = useState(true);
  const memory = useRef<CreativeScrollMemory>({
    categoryOffset: 0, catalogOffsets: new Map(),
  });
  const categories = Array.from({ length: 18 }, (_, i) => ({
    id: "category-" + i, label: "Category " + i,
  }));
  const items = Array.from({ length: 120 }, (_, i) => ({
    id: "asteria:catalog-" + i, name: "Catalog " + i,
  })).filter(item => !searchQuery || item.name.toLowerCase().includes(searchQuery.toLowerCase()));
  return (
    <>
      <button type="button" onClick={() => setMounted(value => !value)}>
        Toggle Creative panel
      </button>
      {mounted && (
        <CreativeInventoryPanel
          state={{ selectedCategoryId, searchQuery, categories, items }}
          scrollMemory={memory.current}
          onCategoryChange={setSelectedCategoryId}
          onSearchChange={setSearchQuery}
          onItemClick={() => undefined}
        />
      )}
    </>
  );
}

export const ScrollMemory: Story = {
  render: () => <ScrollMemoryFlow />,
};
