import type {
  ItemStackView,
} from "../../../presentation/inventoryModels";
import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { TextInput } from "../../atoms/TextInput/TextInput";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
import "./StorageBoxPage.css";

const COLUMNS = 9;
const STORAGE_SLOTS = 27;
const BACKPACK_SLOTS = 27;
const HOTBAR_SLOTS = 9;

export type StorageBoxPageProps = {
  storageSearch: string;
  inventorySearch: string;
  storage: readonly (ItemStackView | null)[];
  backpack: readonly (ItemStackView | null)[];
  hotbar: readonly (ItemStackView | null)[];
  onStorageSearchChange?(value: string): void;
  onInventorySearchChange?(value: string): void;
  onSortStorage?(): void;
  onSortInventory?(): void;
  onStorageSlotClick?(
    index: number,
    item: ItemStackView | null,
  ): void;
  onInventorySlotClick?(
    index: number,
    item: ItemStackView | null,
  ): void;
};

export function StorageBoxPage({
  storageSearch,
  inventorySearch,
  storage,
  backpack,
  hotbar,
  onStorageSearchChange,
  onInventorySearchChange,
  onSortStorage,
  onSortInventory,
  onStorageSlotClick,
  onInventorySlotClick,
}: StorageBoxPageProps) {
  const storageSlots =
    normalizedSlots(
      storage,
      STORAGE_SLOTS,
    );
  const backpackSlots =
    normalizedSlots(
      backpack,
      BACKPACK_SLOTS,
    );
  const hotbarSlots =
    normalizedSlots(
      hotbar,
      HOTBAR_SLOTS,
    );

  return (
    <main className="storage-box-page">
      <div className="storage-box-page__backdrop" />
      <Surface
        variant="hud"
        className="storage-box-page__panel"
      >
        <PanelHeader
          title="Storage Box"
          search={storageSearch}
          searchLabel="Search storage"
          onSearchChange={
            onStorageSearchChange
          }
          onSort={onSortStorage}
        />

        <SlotGrid
          items={storageSlots}
          onClick={
            onStorageSlotClick
          }
        />

        <PanelHeader
          title="Inventory"
          search={inventorySearch}
          searchLabel="Search inventory"
          onSearchChange={
            onInventorySearchChange
          }
          onSort={onSortInventory}
        />

        <SlotGrid
          items={backpackSlots}
          onClick={
            onInventorySlotClick
          }
        />

        <div className="storage-box-page__hotbar">
          {hotbarSlots.map(
            (item, index) => (
              <InventorySlot
                key={index}
                item={item}
                onClick={() =>
                  onInventorySlotClick?.(
                    BACKPACK_SLOTS +
                      index,
                    item,
                  )
                }
              />
            ),
          )}
        </div>
      </Surface>
    </main>
  );
}

function PanelHeader({
  title,
  search,
  searchLabel,
  onSearchChange,
  onSort,
}: {
  title: string;
  search: string;
  searchLabel: string;
  onSearchChange?(
    value: string,
  ): void;
  onSort?(): void;
}) {
  return (
    <header className="storage-box-page__header">
      <Text
        text={title}
        variant="heading"
      />
      <div className="storage-box-page__controls">
        <TextInput
          value={search}
          placeholder={searchLabel}
          aria-label={searchLabel}
          onChange={(event) =>
            onSearchChange?.(
              event.target.value,
            )
          }
        />
        <Button
          label="⇅"
          className="storage-box-page__sort"
          onClick={onSort}
        />
      </div>
    </header>
  );
}

function SlotGrid({
  items,
  onClick,
}: {
  items: readonly (
    ItemStackView |
    null
  )[];
  onClick?(
    index: number,
    item: ItemStackView | null,
  ): void;
}) {
  return (
    <div className="storage-box-page__grid">
      {items.map(
        (item, index) => (
          <InventorySlot
            key={index}
            item={item}
            onClick={() =>
              onClick?.(
                index,
                item,
              )
            }
          />
        ),
      )}
    </div>
  );
}

function normalizedSlots(
  source: readonly (
    ItemStackView |
    null
  )[],
  count: number,
): (ItemStackView | null)[] {
  return Array.from(
    { length: count },
    (_, index) =>
      source[index] ??
      null,
  );
}
