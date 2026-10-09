import { useLayoutEffect, useRef } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  CreativeInventoryView, CreativeScrollMemory, ItemStackView,
} from "../../../presentation/inventoryModels";
import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { TextInput } from "../../atoms/TextInput/TextInput";
import { InventoryHotbarFooter } from "../../molecules/InventoryHotbarFooter/InventoryHotbarFooter";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
import "./CreativeInventoryPanel.css";

export type CreativeInventoryPanelProps = {
  state: CreativeInventoryView;
  hotbar?: readonly (ItemStackView | null)[];
  onHotbarSlotClick?(index: number, item: ItemStackView | null): void;
  onTrash?(): void;
  onSearchChange?(value: string): void;
  onCategoryChange?(id: string | null): void;
  onItemClick?(item: ItemStackView): void;
  scrollMemory?: CreativeScrollMemory;
};

export function CreativeInventoryPanel({
  state, hotbar, onHotbarSlotClick, onTrash,
  onSearchChange, onCategoryChange, onItemClick, scrollMemory,
}: CreativeInventoryPanelProps) {
  const { t } = useLocalization();
  const categoryRef = useRef<HTMLDivElement>(null);
  const gridRef = useRef<HTMLDivElement>(null);
  const scrollKey = JSON.stringify([state.selectedCategoryId, state.searchQuery.trim()]);

  useLayoutEffect(() => {
    if (categoryRef.current)
      categoryRef.current.scrollTop = scrollMemory?.categoryOffset ?? 0;
  }, [scrollMemory]);

  useLayoutEffect(() => {
    if (gridRef.current)
      gridRef.current.scrollTop = scrollMemory?.catalogOffsets.get(scrollKey) ?? 0;
  }, [scrollKey, scrollMemory]);

  const rememberCatalogScroll = (offset: number) => {
    if (!scrollMemory) return;
    const offsets = scrollMemory.catalogOffsets;
    // Keep only 64 recently used category/search combinations.
    offsets.delete(scrollKey);
    offsets.set(scrollKey, offset);
    if (offsets.size > 64)
      offsets.delete(offsets.keys().next().value!);
  };
  return (
    <Surface variant="hud" className="creative-inventory-panel">
      <aside className="creative-inventory-panel__categories">
        <Text text={t("ui.creative")} variant="heading" />
        <div className="creative-inventory-panel__category-list"
          ref={categoryRef}
          onScroll={event => {
            if (scrollMemory)
              scrollMemory.categoryOffset = event.currentTarget.scrollTop;
          }}>
          <Button
            label={t("inventory.everything")}
            iconUrl={state.everythingIconUrl ?? undefined}
            ariaPressed={state.selectedCategoryId === null}
            variant={state.selectedCategoryId === null ? "primary" : "normal"}
            stretch disabled={!onCategoryChange}
            onClick={() => onCategoryChange?.(null)}
          />
          {state.categories.map(category => (
            <Button
              key={category.id}
              label={category.label}
              iconUrl={category.iconUrl}
              ariaPressed={category.id === state.selectedCategoryId}
              variant={category.id === state.selectedCategoryId ? "primary" : "normal"}
              stretch disabled={!onCategoryChange}
              onClick={() => onCategoryChange?.(category.id)}
            />
          ))}
        </div>
      </aside>
      <div className="creative-inventory-panel__content">
        <section className="creative-inventory-panel__catalog">
          <header className="creative-inventory-panel__header">
            <Text text={t("ui.catalog")} variant="heading" />
            <TextInput
              value={state.searchQuery}
              maxLength={128}
              placeholder={t("ui.searchItems")}
              aria-label={t("ui.searchCreativeItems")}
              onChange={event => onSearchChange?.(event.target.value)}
            />
          </header>
          <div className="creative-inventory-panel__grid" ref={gridRef}
            onScroll={event => rememberCatalogScroll(event.currentTarget.scrollTop)}>
            {state.items.map(item => (
              <InventorySlot
                key={[
                  item.kind ?? "", item.id,
                  ...Object.entries(item.metadata ?? {}).sort(([a], [b]) => a.localeCompare(b))
                    .map(([key, value]) => key + "=" + value),
                ].join(":")}
                item={item}
                disabled={!onItemClick}
                onClick={() => onItemClick?.(item)}
              />
            ))}
          </div>
        </section>
        {hotbar && (
          <InventoryHotbarFooter hotbar={hotbar}
            onSlotClick={onHotbarSlotClick} onTrash={onTrash} />
        )}
      </div>
    </Surface>
  );
}
