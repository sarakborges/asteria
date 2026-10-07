import { abbreviateContentId } from "../../../presentation/formatters";
import type { HotbarSlotState } from "../../../state/uiState";
import "./HotbarSlot.css";

export type HotbarSlotProps = {
  index: number;
  state: HotbarSlotState | null;
  selected: boolean;
};

export function HotbarSlot({
  index,
  state,
  selected,
}: HotbarSlotProps) {
  const id = state?.id?.trim() ?? "";
  const quantity =
    state?.quantity ?? 0;
  const classes = [
    "hotbar-slot",
    id !== ""
      ? "hotbar-slot--occupied"
      : "",
    selected
      ? "hotbar-slot--selected"
      : "",
  ]
    .filter(Boolean)
    .join(" ");

  return (
    <div
      className={classes}
      data-slot={index}
      data-item-id={id}
    >
      <span className="hotbar-slot__number">
        {index < 9
          ? index + 1
          : ""}
      </span>
      <span className="hotbar-slot__glyph">
        {id === ""
          ? ""
          : abbreviateContentId(id)}
      </span>
      <span className="hotbar-slot__quantity">
        {quantity > 1
          ? quantity
          : ""}
      </span>
    </div>
  );
}
