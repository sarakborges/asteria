import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  CreativeInventoryView, ItemStackView,
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
};

export function CreativeInventoryPanel({
  state, hotbar, onHotbarSlotClick, onTrash,
  onSearchChange, onCategoryChange, onItemClick,
}: CreativeInventoryPanelProps) {
  const { t } = useLocalization();
  return (
    <Surface variant="hud" className="creative-inventory-panel">
      <aside className="creative-inventory-panel__categories">
        <Text text={t("ui.creative")} variant="heading" />
        <div className="creative-inventory-panel__category-list">
          <Button
            label={t("inventory.everything")}
            ariaPressed={state.selectedCategoryId === null}
            variant={state.selectedCategoryId === null ? "primary" : "normal"}
            stretch disabled={!onCategoryChange}
            onClick={() => onCategoryChange?.(null)}
          />
          {state.categories.map(category => (
            <Button
              key={category.id}
              label={category.label}
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
              placeholder={t("ui.searchItems")}
              aria-label={t("ui.searchCreativeItems")}
              onChange={event => onSearchChange?.(event.target.value)}
            />
          </header>
          <div className="creative-inventory-panel__grid">
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
