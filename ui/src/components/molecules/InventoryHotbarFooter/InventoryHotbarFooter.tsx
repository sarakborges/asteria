import { inventorySearchMatches } from "../../../presentation/inventoryLabels";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { ItemStackView } from "../../../presentation/inventoryModels";
import { InventorySlot } from "../InventorySlot/InventorySlot";
import "./InventoryHotbarFooter.css";

export type InventoryHotbarFooterProps = {
  hotbar: readonly (ItemStackView | null)[];
  onSlotClick?(index: number, item: ItemStackView | null): void;
  onTrash?(): void;
  showTrash?: boolean;
  searchQuery?: string;
};

const HOTBAR_SIZE = 9;
const HOTBAR_OFFSET = 27;

export function InventoryHotbarFooter({
  hotbar, onSlotClick, onTrash, showTrash = true, searchQuery = "",
}: InventoryHotbarFooterProps) {
  const { t, contentName } = useLocalization();
  const query = searchQuery.trim();
  return (
    <div className="inventory-hotbar-footer">
      <div className="inventory-hotbar-footer__slots">
        {Array.from({ length: HOTBAR_SIZE }, (_, index) => {
          const item = hotbar[index] ?? null;
          const filteredOut = Boolean(query) &&
            !inventorySearchMatches(item, query, contentName);
          return (
            <InventorySlot key={index} item={item}
              disabled={!onSlotClick || filteredOut}
              onClick={() => onSlotClick?.(HOTBAR_OFFSET + index, item)} />
          );
        })}
      </div>
      {showTrash && (
        <button type="button" className="inventory-hotbar-footer__trash"
          aria-label={t("ui.trashItem")} title={t("ui.trashItem")}
          disabled={!onTrash} onClick={onTrash}>
          ×
        </button>
      )}
    </div>
  );
}
