import { useMemo, useRef, useState, type PointerEvent } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import { inventoryItemView, type ItemStackView } from "../../../presentation/inventoryModels";
import type { GameplayInventoryState, StorageBoxState } from "../../../state/uiState";
import { Surface } from "../../atoms/Surface/Surface";
import { Button } from "../../atoms/Button/Button";
import { Text } from "../../atoms/Text/Text";
import { InventoryPanelHeader } from "../../molecules/InventoryPanelHeader/InventoryPanelHeader";
import { InventoryHotbarFooter } from "../../molecules/InventoryHotbarFooter/InventoryHotbarFooter";
import { InventoryCursorOverlay } from "../../molecules/InventoryCursorOverlay/InventoryCursorOverlay";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
import "./StorageBoxPage.css";

const STORAGE_SLOTS = 27;
const BACKPACK_SLOTS = 27;
const HOTBAR_SLOTS = 9;

export type StorageBoxPageProps = {
  storage: StorageBoxState;
  inventory: GameplayInventoryState;
  onClose(): void;
  onSortStorage(): void;
  onSortInventory(): void;
  onStorageSlotClick(index: number): void;
  onInventorySlotClick(index: number): void;
};

export function StorageBoxPage({
  storage, inventory, onClose, onSortStorage, onSortInventory,
  onStorageSlotClick, onInventorySlotClick,
}: StorageBoxPageProps) {
  const { t } = useLocalization();
  const cursorRef = useRef<HTMLDivElement>(null);
  const [storageSearch, setStorageSearch] = useState("");
  const [inventorySearch, setInventorySearch] = useState("");

  const { storageSlots, backpackSlots, hotbarSlots, cursor } = useMemo(() => ({
    storageSlots: normalizedSlots(storage.slots, STORAGE_SLOTS)
      .map(item => inventoryItemView(item, inventory.catalog)),
    backpackSlots: normalizedSlots(inventory.backpack, BACKPACK_SLOTS)
      .map(item => inventoryItemView(item, inventory.catalog)),
    hotbarSlots: normalizedSlots(inventory.hotbar, HOTBAR_SLOTS)
      .map(item => inventoryItemView(item, inventory.catalog)),
    cursor: inventoryItemView(inventory.cursor, inventory.catalog),
  }), [storage.slots, inventory.backpack, inventory.hotbar,
    inventory.cursor, inventory.catalog]);

  const followPointer = (event: PointerEvent<HTMLElement>) => {
    const overlay = cursorRef.current;
    if (!overlay) return;
    overlay.style.left = event.clientX - 20 + "px";
    overlay.style.top = event.clientY - 20 + "px";
    overlay.style.visibility = "visible";
  };

  return (
    <main className="storage-box-page" onPointerMove={followPointer}
      onPointerLeave={() => {
        if (cursorRef.current) cursorRef.current.style.visibility = "hidden";
      }}>
      <div className="storage-box-page__backdrop" />
      <Surface variant="hud" className="storage-box-page__panel">
        <div className="storage-box-page__actions">
          <Button label={t("inventory.close")} onClick={onClose} />
        </div>
        <InventoryPanelHeader title={t("storage.title")}
          search={storageSearch} searchLabel={t("storage.search")}
          sortLabel={t("storage.sort")}
          onSearchChange={setStorageSearch} onSort={onSortStorage} />
        <SlotGrid items={storageSlots} searchQuery={storageSearch}
          onClick={onStorageSlotClick} />
        <InventoryPanelHeader title={t("ui.inventory")}
          search={inventorySearch} searchLabel={t("ui.searchInventory")}
          sortLabel={t("ui.sortBackpack")}
          onSearchChange={setInventorySearch} onSort={onSortInventory} />
        <SlotGrid items={backpackSlots} searchQuery={inventorySearch}
          onClick={onInventorySlotClick} />
        <InventoryHotbarFooter hotbar={hotbarSlots} showTrash={false}
          searchQuery={inventorySearch} onSlotClick={onInventorySlotClick} />
      </Surface>
      {cursor && <InventoryCursorOverlay ref={cursorRef} item={cursor} />}
      {storage.errorKey && (
        <aside className="storage-box-page__cursor-status" role="status">
          <Text text={t(storage.errorKey)} variant="caption" />
        </aside>
      )}
    </main>
  );
}

function SlotGrid({
  items, searchQuery, onClick,
}: {
  items: readonly (ItemStackView | null)[];
  searchQuery: string;
  onClick(index: number): void;
}) {
  const query = searchQuery.trim().toLowerCase();
  return (
    <div className="storage-box-page__grid">
      {items.map((item, index) => (
        <InventorySlot key={index} item={item}
          disabled={Boolean(query && !item?.id.toLowerCase().includes(query))}
          onClick={() => onClick(index)} />
      ))}
    </div>
  );
}

function normalizedSlots<T>(source: readonly (T | null)[], count: number): (T | null)[] {
  return Array.from({ length: count }, (_, index) => source[index] ?? null);
}
