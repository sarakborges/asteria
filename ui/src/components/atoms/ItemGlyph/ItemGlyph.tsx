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
  if (item.blockPreview) {
    const preview = item.blockPreview;
    return preview.kind === "cube" && preview.top && preview.right ? (
      <span className={"item-glyph item-glyph--" + size + " item-glyph--cube"} aria-hidden="true">
        <span className="item-glyph__cube-face item-glyph__cube-top"
          style={{ backgroundImage: `url("${preview.top}")` }} />
        <span className="item-glyph__cube-face item-glyph__cube-left"
          style={{ backgroundImage: `url("${preview.front}")` }} />
        <span className="item-glyph__cube-face item-glyph__cube-right"
          style={{ backgroundImage: `url("${preview.right}")` }} />
      </span>
    ) : (
      <img className={"item-glyph item-glyph--" + size}
        src={preview.front} alt="" />
    );
  }

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
