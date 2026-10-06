import "./HotbarSlot.css";

export type HotbarSlotState = {
  id?: string;
  quantity?: number;
};

export type HotbarSlotView = {
  element: HTMLElement;
  setState(state: HotbarSlotState | null): void;
  setSelected(selected: boolean): void;
};

export function createHotbarSlot(index: number): HotbarSlotView {
  const root = document.createElement("div");
  root.className = "hotbar-slot";
  root.dataset.slot = String(index);

  const number = document.createElement("span");
  number.className = "hotbar-slot__number";
  number.textContent = index < 9 ? String(index + 1) : "";

  const glyph = document.createElement("span");
  glyph.className = "hotbar-slot__glyph";

  const quantity = document.createElement("span");
  quantity.className = "hotbar-slot__quantity";

  root.append(number, glyph, quantity);

  return {
    element: root,
    setState(state) {
      const id = state?.id?.trim() ?? "";
      glyph.textContent = id === "" ? "" : abbreviateItemId(id);
      root.dataset.itemId = id;

      const nextQuantity = state?.quantity ?? 0;
      quantity.textContent = nextQuantity > 1 ? String(nextQuantity) : "";
      root.classList.toggle("hotbar-slot--occupied", id !== "");
    },
    setSelected(selected) {
      root.classList.toggle("hotbar-slot--selected", selected);
    },
  };
}

function abbreviateItemId(id: string): string {
  const localId = id.includes(":") ? id.split(":").at(-1) ?? id : id;
  const words = localId.split(/[_-]+/).filter(Boolean);
  if (words.length === 0) return "?";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return words.slice(0, 2).map((word) => word[0]?.toUpperCase() ?? "").join("");
}
