import { forwardRef } from "react";
import type { ItemStackView } from "../../../presentation/inventoryModels";
import { ItemGlyph } from "../../atoms/ItemGlyph/ItemGlyph";
import "./InventoryCursorOverlay.css";

export type InventoryCursorOverlayProps = {
  item: ItemStackView;
  /** Preview-only position; runtime moves the overlay with the active UI surface. */
  previewPosition?: { x: number; y: number };
};

export const InventoryCursorOverlay = forwardRef<HTMLDivElement, InventoryCursorOverlayProps>(
  function InventoryCursorOverlay({ item, previewPosition }, ref) {
    return (
      <div
        ref={ref}
        className="inventory-cursor-overlay"
        style={previewPosition ? {
          left: previewPosition.x,
          top: previewPosition.y,
          visibility: "visible",
        } : undefined}
        aria-hidden="true"
      >
        <ItemGlyph item={item} />
        {(item.quantity ?? 0) > 1 && (
          <span className="inventory-cursor-overlay__quantity">{item.quantity}</span>
        )}
      </div>
    );
  },
);
