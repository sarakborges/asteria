import type { Ref } from "react";
import { createPortal } from "react-dom";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { ItemStackView } from "../../../presentation/inventoryModels";
import "./ItemTooltip.css";

export type ItemTooltipProps = {
  item: ItemStackView;
  tooltipRef?: Ref<HTMLDivElement>;
  previewPosition?: { x: number; y: number };
};

export function ItemTooltip({ item, tooltipRef, previewPosition }: ItemTooltipProps) {
  const { contentName } = useLocalization();
  const metadata = Object.entries(item.metadata ?? {})
    .sort(([left], [right]) => left < right ? -1 : left > right ? 1 : 0);
  return createPortal(
    <div ref={tooltipRef} className="item-tooltip" role="tooltip"
      style={previewPosition ? { left: previewPosition.x, top: previewPosition.y } : undefined}>
      <strong className="item-tooltip__title">{item.name || contentName(item.id)}</strong>
      <span className="item-tooltip__id">{item.id}</span>
      {metadata.length > 0 && (
        <dl className="item-tooltip__metadata">
          {metadata.map(([key, value]) => (
            <div className="item-tooltip__property" key={key}>
              <dt>{key}</dt><dd>{value}</dd>
            </div>
          ))}
        </dl>
      )}
    </div>,
    document.body,
  );
}
