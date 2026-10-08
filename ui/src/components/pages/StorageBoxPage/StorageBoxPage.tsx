import { useLocalization } from "../../../localization/LocalizationProvider";
import type { ItemStackView } from "../../../presentation/inventoryModels";
import { Surface } from "../../atoms/Surface/Surface";
import { InventoryPanelHeader } from "../../molecules/InventoryPanelHeader/InventoryPanelHeader";
import { InventoryHotbarFooter } from "../../molecules/InventoryHotbarFooter/InventoryHotbarFooter";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
import "./StorageBoxPage.css";

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
  onStorageSlotClick?(index: number, item: ItemStackView | null): void;
  onInventorySlotClick?(index: number, item: ItemStackView | null): void;
};

export function StorageBoxPage({
  storageSearch, inventorySearch, storage, backpack, hotbar,
  onStorageSearchChange, onInventorySearchChange,
  onSortStorage, onSortInventory, onStorageSlotClick, onInventorySlotClick,
}: StorageBoxPageProps) {
  const { t } = useLocalization();
  const storageSlots = normalizedSlots(storage, STORAGE_SLOTS);
  const backpackSlots = normalizedSlots(backpack, BACKPACK_SLOTS);
  const hotbarSlots = normalizedSlots(hotbar, HOTBAR_SLOTS);

  return (
    <main className="storage-box-page">
      <div className="storage-box-page__backdrop" />
      <Surface variant="hud" className="storage-box-page__panel">
        <InventoryPanelHeader title={t("storage.title")}
          search={storageSearch} searchLabel={t("storage.search")}
          sortLabel={t("storage.sort")}
          onSearchChange={onStorageSearchChange} onSort={onSortStorage} />
        <SlotGrid items={storageSlots} searchQuery={storageSearch}
          onClick={onStorageSlotClick} />
        <InventoryPanelHeader title={t("ui.inventory")}
          search={inventorySearch} searchLabel={t("ui.searchInventory")}
          sortLabel={t("ui.sortBackpack")}
          onSearchChange={onInventorySearchChange} onSort={onSortInventory} />
        <SlotGrid items={backpackSlots} searchQuery={inventorySearch}
          onClick={onInventorySlotClick} />
        <InventoryHotbarFooter hotbar={hotbarSlots} showTrash={false}
          searchQuery={inventorySearch} onSlotClick={onInventorySlotClick} />
      </Surface>
    </main>
  );
}

function SlotGrid({
  items, searchQuery, onClick,
}: {
  items: readonly (ItemStackView | null)[];
  searchQuery: string;
  onClick?(index: number, item: ItemStackView | null): void;
}) {
  const query = searchQuery.trim().toLowerCase();
  return (
    <div className="storage-box-page__grid">
      {items.map((item, index) => (
        <InventorySlot key={index} item={item}
          disabled={!onClick || Boolean(query && !item?.id.toLowerCase().includes(query))}
          onClick={() => onClick?.(index, item)} />
      ))}
    </div>
  );
}

function normalizedSlots(
  source: readonly (ItemStackView | null)[],
  count: number,
): (ItemStackView | null)[] {
  return Array.from({ length: count }, (_, index) => source[index] ?? null);
}
