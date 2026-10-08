import { useLocalization } from "../../../localization/LocalizationProvider";
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
  const { contentName, t } = useLocalization();
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
        item?.id
          ? [
            contentName(item.id),
            ...Object.entries(item.metadata ?? {}).map(([key, value]) =>
              key + ": " + value),
          ].join(" · ")
          : t("ui.emptySlot")
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
