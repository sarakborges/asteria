import { abbreviateContentId } from "../../../presentation/formatters";
import type { ItemStackView } from "../../../presentation/inventoryModels";
import "./ItemGlyph.css";

export type ItemGlyphProps = {
  item: ItemStackView;
  size?: "slot" | "recipe" | "result" | "station";
};

export function ItemGlyph({
  item,
  size = "slot",
}: ItemGlyphProps) {
  if (item.iconUrl) {
    return (
      <img
        className={"item-glyph item-glyph--" + size}
        src={item.iconUrl}
        alt=""
      />
    );
  }

  return (
    <span
      className={"item-glyph item-glyph--" + size}
      aria-hidden="true"
    >
      {abbreviateContentId(item.id)}
    </span>
  );
}
