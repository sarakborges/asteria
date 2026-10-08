import { useMemo, useState } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  GameplayInventoryState, InventoryCatalogEntry, InventoryMetadata, VitalValue,
} from "../../../state/uiState";
import type { ItemStackView } from "../../../presentation/inventoryModels";
import { Button } from "../../atoms/Button/Button";
import { Text } from "../../atoms/Text/Text";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
import { CharacterInfoPanel } from "../../organisms/CharacterInfoPanel/CharacterInfoPanel";
import { CraftingPanel } from "../../organisms/CraftingPanel/CraftingPanel";
import { CreativeInventoryPanel } from "../../organisms/CreativeInventoryPanel/CreativeInventoryPanel";
import { CurrentStationPanel } from "../../organisms/CurrentStationPanel/CurrentStationPanel";
import { PlayerInventoryPanel } from "../../organisms/PlayerInventoryPanel/PlayerInventoryPanel";
import { InventoryWorkspace } from "../../templates/InventoryWorkspace/InventoryWorkspace";
import "./InventoryGameplayPage.css";

export type InventoryGameplayPageProps = {
  state: GameplayInventoryState;
  health?: VitalValue | null;
  onClose(): void;
  onSlotClick(index: number): void;
  onSort(): void;
  onDiscardCursor(): void;
  onCreativePick(choice: InventoryCatalogEntry): void;
};

function sameMetadata(a: InventoryMetadata, b: InventoryMetadata): boolean {
  const keys = Object.keys(a);
  return keys.length === Object.keys(b).length &&
    keys.every(key => a[key] === b[key]);
}

function findCatalogEntry(
  slot: NonNullable<GameplayInventoryState["cursor"]>,
  catalog: GameplayInventoryState["catalog"],
): InventoryCatalogEntry | undefined {
  return catalog.find(choice => choice.id === slot.id &&
    choice.kind === slot.kind && sameMetadata(choice.metadata, slot.metadata));
}

function itemView(
  slot: GameplayInventoryState["cursor"],
  catalog: GameplayInventoryState["catalog"],
): ItemStackView | null {
  if (!slot) return null;
  const authored = findCatalogEntry(slot, catalog);
  return {
    id: slot.id, kind: slot.kind, quantity: slot.quantity,
    metadata: slot.metadata, iconUrl: authored?.iconUrl,
  };
}

/**
 * In-game inventory and Storybook use the same panel composition.
 * Only Godot-sourced slots and vitals are displayed as gameplay facts.
 */
export function InventoryGameplayPage({
  state, health, onClose, onSlotClick, onSort,
  onDiscardCursor, onCreativePick,
}: InventoryGameplayPageProps) {
  const { t } = useLocalization();
  const [creativeTab, setCreativeTab] = useState(false);
  const [search, setSearch] = useState("");
  const [creativeSearch, setCreativeSearch] = useState("");
  const [category, setCategory] = useState<string | null>(null);
  const categories = useMemo(
    () => [...new Set(state.catalog.map(item => item.category))]
      .sort((a, b) => a < b ? -1 : a > b ? 1 : 0)
      .map(id => ({ id, label: id })),
    [state.catalog],
  );
  const creative = state.creativeAvailable && creativeTab;
  const creativeItems = useMemo(() => {
    const query = creativeSearch.trim().toLowerCase();
    return state.catalog.filter(item =>
      (category === null || item.category === category) &&
      (item.name.toLowerCase().includes(query) || item.id.toLowerCase().includes(query)),
    ).map(item => ({
      ...item,
      quantity: 1,
      name: Object.keys(item.metadata).length > 0
        ? item.name + " (" + Object.values(item.metadata).join(", ") + ")"
        : item.name,
    }));
  }, [state.catalog, category, creativeSearch]);
  const { backpack, hotbar, cursor } = useMemo(() => ({
    backpack: state.backpack.map(slot => itemView(slot, state.catalog)),
    hotbar: state.hotbar.map(slot => itemView(slot, state.catalog)),
    cursor: itemView(state.cursor, state.catalog),
  }), [state.backpack, state.hotbar, state.cursor, state.catalog]);

  return (
    <main className="inventory-gameplay">
      <div className="inventory-gameplay__actions">
        <Button label={t("inventory.close")} onClick={onClose} />
      </div>
      <div className="inventory-gameplay__workspace">
        <InventoryWorkspace
          creativeAvailable={state.creativeAvailable}
          creativeVisible={creative}
          onViewChange={setCreativeTab}
          character={<CharacterInfoPanel state={health ? {
            name: t("ui.player"),
            healthCurrent: health.current,
            healthMaximum: health.maximum,
            equipment: [],
          } : null} />}
          crafting={<CraftingPanel recipes={[]} selectedRecipeId={null}
            status={t("inventory.craftingUnavailable")} />}
          inventory={<PlayerInventoryPanel
            state={{ searchQuery: search, backpack, hotbar }}
            onSearchChange={setSearch}
            onSort={onSort}
            onTrash={cursor ? onDiscardCursor : undefined}
            onSlotClick={index => onSlotClick(index)}
          />}
          station={<CurrentStationPanel station={null} />}
          creative={<CreativeInventoryPanel
            state={{
              searchQuery: creativeSearch,
              selectedCategoryId: category,
              categories,
              items: creativeItems,
            }}
            hotbar={hotbar}
            onHotbarSlotClick={index => onSlotClick(index)}
            onTrash={cursor ? onDiscardCursor : undefined}
            onSearchChange={setCreativeSearch}
            onCategoryChange={setCategory}
            onItemClick={item => {
              const choice = state.catalog.find(entry =>
                entry.id === item.id && entry.kind === item.kind &&
                sameMetadata(entry.metadata, item.metadata ?? {}));
              if (choice) onCreativePick(choice);
            }}
          />}
        />
      </div>
      {(cursor || state.errorKey) && (
        <aside className="inventory-gameplay__cursor-status" role="status">
          {cursor && (
            <>
              <Text text={t("inventory.cursor")} variant="detail" />
              <InventorySlot item={cursor} disabled />
              <Text text={t("inventory.cursor.help")} variant="caption" />
            </>
          )}
          {state.errorKey && <Text text={t(state.errorKey)} variant="caption" />}
        </aside>
      )}
    </main>
  );
}
