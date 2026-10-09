import { useLayoutEffect, useRef, useState, type PointerEvent } from "react";
import { inventoryDisplayName } from "../../../presentation/inventoryLabels";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { ItemStackView } from "../../../presentation/inventoryModels";
import { ItemGlyph } from "../../atoms/ItemGlyph/ItemGlyph";
import { ItemTooltip } from "../ItemTooltip/ItemTooltip";
import "./InventorySlot.css";

export type InventorySlotProps = {
  item?: ItemStackView | null;
  selected?: boolean;
  disabled?: boolean;
  onClick?(): void;
};

const TOOLTIP_OFFSET = 16;
const VIEWPORT_MARGIN = 8;

export function InventorySlot({
  item = null, selected = false, disabled = false, onClick,
}: InventorySlotProps) {
  const { contentName, t } = useLocalization();
  const [showTooltip, setShowTooltip] = useState(false);
  const tooltipRef = useRef<HTMLDivElement>(null);
  const lastPointer = useRef({ x: 0, y: 0 });

  const moveTooltip = (x: number, y: number) => {
    lastPointer.current = { x, y };
    const tooltip = tooltipRef.current;
    if (!tooltip) return;

    // Measure after mounting; keep the tooltip inside the visible viewport.
    const width = tooltip.offsetWidth;
    const height = tooltip.offsetHeight;
    const left = Math.min(x + TOOLTIP_OFFSET,
      Math.max(VIEWPORT_MARGIN, window.innerWidth - width - VIEWPORT_MARGIN));
    const top = Math.min(y + TOOLTIP_OFFSET,
      Math.max(VIEWPORT_MARGIN, window.innerHeight - height - VIEWPORT_MARGIN));
    tooltip.style.left = Math.max(VIEWPORT_MARGIN, left) + "px";
    tooltip.style.top = Math.max(VIEWPORT_MARGIN, top) + "px";
  };

  useLayoutEffect(() => {
    if (showTooltip && item) moveTooltip(lastPointer.current.x, lastPointer.current.y);
  }, [showTooltip, item]);

  const onMove = (event: PointerEvent<HTMLButtonElement>) => {
    moveTooltip(event.clientX, event.clientY);
  };
  const name = item
    ? inventoryDisplayName(item.id, item.metadata, contentName, item.name)
    : t("ui.emptySlot");

  return (
    <>
      <button
        type="button"
        className={["inventory-slot", selected ? "inventory-slot--selected" : ""]
          .filter(Boolean).join(" ")}
        disabled={disabled}
        aria-label={item
          ? [name, ...Object.entries(item.metadata ?? {}).map(([key, value]) =>
            key + ": " + value)].join(" · ")
          : name}
        onClick={onClick}
        onPointerEnter={event => {
          if (item && event.pointerType !== "touch") {
            lastPointer.current = { x: event.clientX, y: event.clientY };
            setShowTooltip(true);
          }
        }}
        onPointerMove={onMove}
        onPointerLeave={() => setShowTooltip(false)}
        onFocus={event => {
          if (!item) return;
          const rect = event.currentTarget.getBoundingClientRect();
          lastPointer.current = { x: rect.right, y: rect.top };
          setShowTooltip(true);
        }}
        onBlur={() => setShowTooltip(false)}
      >
        {item && (
          <>
            <ItemGlyph item={item} />
            {(item.quantity ?? 0) > 1 && (
              <span className="inventory-slot__quantity">{item.quantity}</span>
            )}
            {item.durability && (
              <span className="inventory-slot__durability" aria-hidden="true">
                <span
                  className="inventory-slot__durability-fill"
                  style={{
                    width: 100 * item.durability.current /
                      item.durability.maximum + "%",
                  }}
                />
              </span>
            )}
          </>
        )}
      </button>
      {item && showTooltip && <ItemTooltip item={item} tooltipRef={tooltipRef} />}
    </>
  );
}
