import { inventorySearchMatches } from "../../../presentation/inventoryLabels";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  ItemStackView,
  PlayerInventoryView,
} from "../../../presentation/inventoryModels";
import { Surface } from "../../atoms/Surface/Surface";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
import { InventoryHotbarFooter } from "../../molecules/InventoryHotbarFooter/InventoryHotbarFooter";
import { InventoryPanelHeader } from "../../molecules/InventoryPanelHeader/InventoryPanelHeader";
import "./PlayerInventoryPanel.css";

const BACKPACK_COLUMNS = 9;
const BACKPACK_ROWS = 3;
const HOTBAR_COLUMNS = 9;

export type PlayerInventoryPanelProps = {
  state: PlayerInventoryView;
  onSearchChange?(value: string): void;
  onSort?(): void;
  onTrash?(): void;
  onSlotClick?(
    index: number,
    item: ItemStackView | null,
  ): void;
};

export function PlayerInventoryPanel({
  state,
  onSearchChange,
  onSort,
  onTrash,
  onSlotClick,
}: PlayerInventoryPanelProps) {
  const { t, contentName } = useLocalization();
  const backpack = normalizedSlots(
    state.backpack,
    BACKPACK_COLUMNS *
      BACKPACK_ROWS,
  );
  const hotbar = normalizedSlots(
    state.hotbar,
    HOTBAR_COLUMNS,
  );

  return (
    <Surface
      variant="hud"
      className="player-inventory-panel"
    >
      <InventoryPanelHeader
        title={t("ui.inventory")}
        search={state.searchQuery}
        searchLabel={t("ui.searchInventory")}
        sortLabel={t("ui.sortBackpack")}
        onSearchChange={onSearchChange}
        onSort={onSort}
      />

      <div className="player-inventory-panel__backpack">
        {backpack.map(
          (item, index) => (
            <InventorySlot
              key={index}
              item={item}
              disabled={Boolean(state.searchQuery.trim()) &&
                !inventorySearchMatches(item, state.searchQuery, contentName)}
              onClick={() =>
                onSlotClick?.(
                  index,
                  item,
                )
              }
            />
          ),
        )}
      </div>

      <InventoryHotbarFooter
        hotbar={hotbar}
        searchQuery={state.searchQuery}
        onSlotClick={onSlotClick}
        onTrash={onTrash}
      />
    </Surface>
  );
}

function normalizedSlots(
  source:
    readonly (
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
