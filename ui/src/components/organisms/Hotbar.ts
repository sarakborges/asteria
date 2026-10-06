import {
  createHotbarSlot,
  type HotbarSlotState,
  type HotbarSlotView,
} from "../molecules/HotbarSlot";
import "./Hotbar.css";

const DEFAULT_SLOT_COUNT = 9;

export type HotbarState = {
  slots?: HotbarSlotState[];
  selectedIndex?: number | null;
  selectedName?: string | null;
};

export type HotbarView = {
  element: HTMLElement;
  setState(state: HotbarState): void;
};

export function createHotbar(slotCount = DEFAULT_SLOT_COUNT): HotbarView {
  const root = document.createElement("section");
  root.className = "hotbar";

  const selectedName = document.createElement("div");
  selectedName.className = "hotbar__selected-name";

  const row = document.createElement("div");
  row.className = "hotbar__row";

  const slots: HotbarSlotView[] = [];
  for (let index = 0; index < slotCount; index += 1) {
    const slot = createHotbarSlot(index);
    slot.setState(null);
    row.append(slot.element);
    slots.push(slot);
  }

  root.append(selectedName, row);

  return {
    element: root,
    setState(state) {
      const nextSlots = state.slots ?? [];
      const selectedIndex = normalizeSelectedIndex(
        state.selectedIndex ?? null,
        slots.length,
      );

      for (let index = 0; index < slots.length; index += 1) {
        slots[index].setState(nextSlots[index] ?? null);
        slots[index].setSelected(index === selectedIndex);
      }

      selectedName.textContent =
        state.selectedName?.trim() ??
        itemDisplayName(nextSlots[selectedIndex ?? -1]?.id);
    },
  };
}

function normalizeSelectedIndex(
  index: number | null,
  slotCount: number,
): number | null {
  if (index === null || !Number.isInteger(index)) return null;
  if (index < 0 || index >= slotCount) return null;
  return index;
}

function itemDisplayName(id: string | undefined): string {
  if (!id) return "";
  const localId = id.includes(":") ? id.split(":").at(-1) ?? id : id;
  return localId
    .split(/[_-]+/)
    .filter(Boolean)
    .map((word) => word[0]?.toUpperCase() + word.slice(1))
    .join(" ");
}
