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
import { InventorySlot, type InventorySlotAction } from "../../molecules/InventorySlot/InventorySlot";
import { ScrollArea } from "../../molecules/ScrollArea/ScrollArea";
import "./CreativeInventoryPanel.css";

export type CreativeInventoryPanelProps = {
  state: CreativeInventoryView;
  hotbar?: readonly (ItemStackView | null)[];
  onHotbarSlotClick?(index: number, item: ItemStackView | null, action: InventorySlotAction): void;
  onTrash?(clearAll: boolean): void;
  onSearchChange?(value: string): void;
  onCategoryChange?(id: string | null): void;
  onItemClick?(item: ItemStackView, fillStack: boolean): void;
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
    // A keyboard PageDown can still be animating when a category/search
    // changes. An instant restore cancels the browser's pending scroll,
    // instead of letting it move the newly selected catalog off its start.
    gridRef.current?.scrollTo({
      top: scrollMemory?.catalogOffsets.get(scrollKey) ?? 0,
      behavior: "instant",
    });
  }, [scrollKey, scrollMemory]);

  const selectCategory = (id: string | null) => {
    if (!onCategoryChange) return;
    // Stop an in-flight native keyboard/wheel scroll synchronously, before
    // React commits the new scroll key. Preserve its independent offset.
    const nextKey = JSON.stringify([id, state.searchQuery.trim()]);
    gridRef.current?.scrollTo({
      top: scrollMemory?.catalogOffsets.get(nextKey) ?? 0,
      behavior: "instant",
    });
    onCategoryChange(id);
  };

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
        <ScrollArea className="creative-inventory-panel__category-scroll"
          viewportClassName="creative-inventory-panel__category-list"
          contentClassName="creative-inventory-panel__category-content"
          ariaLabel={t("ui.creative")} viewportRef={categoryRef}
          onViewportScroll={event => {
            if (scrollMemory)
              scrollMemory.categoryOffset = event.currentTarget.scrollTop;
          }}>
          <Button
            label={t("inventory.everything")}
            iconUrl={state.everythingIconUrl ?? undefined}
            ariaPressed={state.selectedCategoryId === null}
            variant={state.selectedCategoryId === null ? "primary" : "normal"}
            stretch disabled={!onCategoryChange}
            onClick={() => selectCategory(null)}
          />
          {state.categories.map(category => (
            <Button
              key={category.id}
              label={category.label}
              iconUrl={category.iconUrl}
              ariaPressed={category.id === state.selectedCategoryId}
              variant={category.id === state.selectedCategoryId ? "primary" : "normal"}
              stretch disabled={!onCategoryChange}
              onClick={() => selectCategory(category.id)}
            />
          ))}
        </ScrollArea>
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
          <ScrollArea className="creative-inventory-panel__grid-scroll"
            viewportClassName="creative-inventory-panel__grid"
            contentClassName="creative-inventory-panel__grid-content"
            ariaLabel={t("ui.catalog")} viewportRef={gridRef}
            onViewportScroll={event => rememberCatalogScroll(event.currentTarget.scrollTop)}>
            {state.items.map(item => (
              <InventorySlot
                key={[
                  item.kind ?? "", item.id,
                  ...Object.entries(item.metadata ?? {}).sort(([a], [b]) => a.localeCompare(b))
                    .map(([key, value]) => key + "=" + value),
                ].join(":")}
                item={item}
                disabled={!onItemClick}
                onClick={action => {
                  if (action !== "right") onItemClick?.(item, action === "quick");
                }}
              />
            ))}
          </ScrollArea>
        </section>
        {hotbar && (
          <InventoryHotbarFooter hotbar={hotbar}
            onSlotClick={onHotbarSlotClick} onTrash={onTrash} />
        )}
      </div>
    </Surface>
  );
}
