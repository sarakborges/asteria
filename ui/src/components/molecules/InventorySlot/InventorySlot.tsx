import type { ItemStackView } from "../../../presentation/inventoryModels";
import { ItemGlyph } from "../../atoms/ItemGlyph/ItemGlyph";
import "./InventorySlot.css";

export type InventorySlotProps = {
  item?: ItemStackView | null;
  selected?: boolean;
  disabled?: boolean;
  onClick?(): void;
};

export function InventorySlot({
  item = null,
  selected = false,
  disabled = false,
  onClick,
}: InventorySlotProps) {
  return (
    <button
      type="button"
      className={[
        "inventory-slot",
        selected
          ? "inventory-slot--selected"
          : "",
      ]
        .filter(Boolean)
        .join(" ")}
      disabled={disabled}
      aria-label={
        item?.name ??
        item?.id ??
        "Empty inventory slot"
      }
      onClick={onClick}
    >
      {item && (
        <>
          <ItemGlyph item={item} />
          {(item.quantity ?? 0) > 1 && (
            <span className="inventory-slot__quantity">
              {item.quantity}
            </span>
          )}
        </>
      )}
    </button>
  );
}
