import { useLocalization } from "../../../localization/LocalizationProvider";
import type { HotbarState } from "../../../state/uiState";
import { HotbarSlot } from "../../molecules/HotbarSlot/HotbarSlot";
import "./Hotbar.css";

const DEFAULT_SLOT_COUNT = 9;

export type HotbarProps = {
  state: HotbarState;
  slotCount?: number;
};

export function Hotbar({
  state,
  slotCount = DEFAULT_SLOT_COUNT,
}: HotbarProps) {
  const { contentName } = useLocalization();
  const selectedIndex =
    normalizeSelectedIndex(
      state.selectedIndex,
      slotCount,
    );
  const selectedId = state.slots[selectedIndex ?? -1]?.id;
  const selectedName = selectedId ? contentName(selectedId) : (state.selectedName?.trim() ?? "");

  return (
    <section className="hotbar">
      <div className="hotbar__selected-name">
        {selectedName}
      </div>
      <div className="hotbar__row">
        {Array.from(
          { length: slotCount },
          (_, index) => (
            <HotbarSlot
              key={index}
              index={index}
              state={state.slots[index] ?? null}
              selected={index === selectedIndex}
            />
          ),
        )}
      </div>
    </section>
  );
}

function normalizeSelectedIndex(
  index: number | null,
  slotCount: number,
): number | null {
  if (
    index === null ||
    !Number.isInteger(index) ||
    index < 0 ||
    index >= slotCount
  ) {
    return null;
  }

  return index;
}
