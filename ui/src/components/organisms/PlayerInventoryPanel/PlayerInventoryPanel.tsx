import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  ItemStackView,
  PlayerInventoryView,
} from "../../../presentation/inventoryModels";
import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { TextInput } from "../../atoms/TextInput/TextInput";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
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
  const { t } = useLocalization();
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
      <header className="player-inventory-panel__header">
        <Text
          text={t("ui.inventory")}
          variant="heading"
        />
        <div className="player-inventory-panel__controls">
          <TextInput
            value={
              state.searchQuery
            }
            placeholder={t("ui.searchInventory")}
            aria-label={t("ui.searchInventory")}
            onChange={
              (event) =>
                onSearchChange?.(
                  event.target
                    .value,
                )
            }
          />
          <Button
            label="⇅"
            className="player-inventory-panel__sort"
            onClick={onSort}
          />
        </div>
      </header>

      <div className="player-inventory-panel__backpack">
        {backpack.map(
          (item, index) => (
            <InventorySlot
              key={index}
              item={item}
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

      <div className="player-inventory-panel__footer">
        <div className="player-inventory-panel__hotbar">
          {hotbar.map(
            (item, index) => (
              <InventorySlot
                key={index}
                item={item}
                onClick={() =>
                  onSlotClick?.(
                    backpack.length +
                      index,
                    item,
                  )
                }
              />
            ),
          )}
        </div>
        <button
          type="button"
          className="player-inventory-panel__trash"
          aria-label={t("ui.trashItem")}
          onClick={onTrash}
        >
          ×
        </button>
      </div>
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
