import {
  useState,
} from "react";
import type {
  Meta,
  StoryObj,
} from "@storybook/react-vite";
import { InventoryPage } from "./InventoryPage";

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

function Preview({
  creativeAvailable,
}: {
  creativeAvailable: boolean;
}) {
  const [
    creativeVisible,
    setCreativeVisible,
  ] =
    useState(false);

  return (
    <InventoryPage
      creativeAvailable={
        creativeAvailable
      }
      creativeVisible={
        creativeVisible
      }
      onViewChange={
        setCreativeVisible
      }
      character={{
        name: "Player",
        healthCurrent: 86,
        healthMaximum: 100,
        equipment: [
          {
            slot: "helm",
            emptyLabel:
              "No helm equipped",
          },
          {
            slot: "chest",
            emptyLabel:
              "No breastplate equipped",
          },
          {
            slot: "legs",
            emptyLabel:
              "No leggings equipped",
          },
          {
            slot: "boots",
            emptyLabel:
              "No boots equipped",
          },
        ],
      }}
      inventory={{
        searchQuery: "",
        backpack: [
          {
            id:
              "asteria:stone",
            name: "Stone",
            quantity: 64,
          },
          {
            id:
              "asteria:oak_log",
            name: "Oak Log",
            quantity: 6,
          },
        ],
        hotbar: [
          {
            id:
              "asteria:grass_block",
            name:
              "Grass Block",
            quantity: 12,
          },
        ],
      }}
      creative={{
        searchQuery: "",
        selectedCategoryId:
          null,
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
      }}
      recipes={[
        {
          id:
            "asteria:planks",
          result: {
            id:
              "asteria:oak_planks",
            name:
              "Oak Planks",
          },
          outputQuantity: 4,
          ingredients: [
            {
              item: {
                id:
                  "asteria:oak_log",
                name:
                  "Oak Log",
              },
              required: 1,
              available: 6,
            },
          ],
          craftable: true,
        },
      ]}
      selectedRecipeId="asteria:planks"
      station={{
        eyebrow: "BASE STATION",
        name: "Inventory",
        description:
          "Personal crafting",
      }}
    />
  );
}

const meta = {
  title: "Pages/InventoryPage",
  component: InventoryPage,
  parameters: {
    layout: "fullscreen",
  },
  render: () => (
    <Preview
      creativeAvailable
    />
  ),
} satisfies Meta<typeof InventoryPage>;

export default meta;
type Story = StoryObj<typeof meta>;

export const CreativeCapable: Story = {};

export const SurvivalOnly: Story = {
  render: () => (
    <Preview
      creativeAvailable={
        false
      }
    />
  ),
};
