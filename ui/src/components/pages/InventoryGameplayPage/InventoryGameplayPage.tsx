import { useMemo, useState } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { GameplayInventoryState } from "../../../state/uiState";
import type { ItemStackView } from "../../../presentation/inventoryModels";
import { Button } from "../../atoms/Button/Button";
import { Text } from "../../atoms/Text/Text";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
import { PlayerInventoryPanel } from "../../organisms/PlayerInventoryPanel/PlayerInventoryPanel";
import { CreativeInventoryPanel } from "../../organisms/CreativeInventoryPanel/CreativeInventoryPanel";
import "./InventoryGameplayPage.css";

export type InventoryGameplayPageProps = {
  state: GameplayInventoryState;
  onClose(): void;
  onSlotClick(index: number): void;
  onSort(): void;
  onDiscardCursor(): void;
  onCreativePick(id: string): void;
};

function itemView(
  slot: GameplayInventoryState["cursor"],
): ItemStackView | null {
  return slot ? { id: slot.id, quantity: slot.quantity } : null;
}

/**
 * Presentation-only inventory surface. Slot state and cursor mutations are
 * published by Core; local filters/tabs do not manufacture gameplay data.
 */
export function InventoryGameplayPage({
  state, onClose, onSlotClick, onSort,
  onDiscardCursor, onCreativePick,
}: InventoryGameplayPageProps) {
  const { t } = useLocalization();
  const [creativeTab, setCreativeTab] = useState(false);
  const [search, setSearch] = useState("");
  const [creativeSearch, setCreativeSearch] = useState("");
  const [category, setCategory] = useState<string | null>(null);
  const categories = useMemo(
    () => [...new Set(state.catalog.map(item => item.category))]
      .sort((a, b) => a.localeCompare(b))
      .map(id => ({ id, label: id })),
    [state.catalog],
  );
  const creative = state.creativeAvailable && creativeTab;
  const matches = (id: string) =>
    id.toLowerCase().includes(search.trim().toLowerCase());
  const filteredSlots = (slots: GameplayInventoryState["backpack"]) =>
    slots.map(slot => slot && matches(slot.id) ? itemView(slot) : null);
  const creativeItems = state.catalog.filter(item =>
    (category === null || item.category === category) &&
    item.id.toLowerCase().includes(creativeSearch.trim().toLowerCase()),
  );

  return (
    <main className="inventory-gameplay">
      <header className="inventory-gameplay__header">
        <Text text={t("ui.inventory")} variant="heading" />
        <div className="inventory-gameplay__header-actions">
          {state.creativeAvailable && (
            <>
              <Button
                label={t("ui.inventory")}
                variant={!creative ? "primary" : "normal"}
                onClick={() => setCreativeTab(false)} />
              <Button
                label={t("ui.creative")}
                variant={creative ? "primary" : "normal"}
                onClick={() => setCreativeTab(true)} />
            </>
          )}
          <Button label={t("inventory.close")} onClick={onClose} />
        </div>
      </header>

      <section className="inventory-gameplay__content">
        {creative ? (
          <CreativeInventoryPanel
            state={{
              searchQuery: creativeSearch,
              selectedCategoryId: category,
              categories,
              items: creativeItems,
            }}
            onSearchChange={setCreativeSearch}
            onCategoryChange={setCategory}
            onItemClick={item => onCreativePick(item.id)}
          />
        ) : (
          <PlayerInventoryPanel
            state={{
              searchQuery: search,
              backpack: filteredSlots(state.backpack),
              hotbar: filteredSlots(state.hotbar),
            }}
            onSearchChange={setSearch}
            onSort={onSort}
            onTrash={state.cursor ? onDiscardCursor : undefined}
            onSlotClick={onSlotClick}
          />
        )}
      </section>

      <footer className="inventory-gameplay__footer">
        <Text text={t("inventory.cursor")} variant="label" />
        <InventorySlot item={itemView(state.cursor)} disabled />
        {state.cursor && <Text text={t("inventory.cursor.help")} variant="caption" />}
        {state.errorKey && <Text text={t(state.errorKey)} variant="caption" />}
      </footer>
    </main>
  );
}
