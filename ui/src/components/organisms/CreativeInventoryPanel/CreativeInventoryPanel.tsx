import type {
  CreativeInventoryView,
  ItemStackView,
} from "../../../presentation/inventoryModels";
import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { TextInput } from "../../atoms/TextInput/TextInput";
import { InventorySlot } from "../../molecules/InventorySlot/InventorySlot";
import "./CreativeInventoryPanel.css";

export type CreativeInventoryPanelProps = {
  state: CreativeInventoryView;
  onSearchChange?(value: string): void;
  onCategoryChange?(
    id: string | null,
  ): void;
  onItemClick?(
    item: ItemStackView,
  ): void;
};

export function CreativeInventoryPanel({
  state,
  onSearchChange,
  onCategoryChange,
  onItemClick,
}: CreativeInventoryPanelProps) {
  return (
    <Surface
      variant="hud"
      className="creative-inventory-panel"
    >
      <aside className="creative-inventory-panel__categories">
        <Text
          text="Creative"
          variant="heading"
        />
        <div className="creative-inventory-panel__category-list">
          <Button
            label="Everything"
            variant={
              state.selectedCategoryId ===
              null
                ? "primary"
                : "normal"
            }
            stretch
            onClick={() =>
              onCategoryChange?.(
                null,
              )
            }
          />
          {state.categories.map(
            (category) => (
              <Button
                key={
                  category.id
                }
                label={
                  category.label
                }
                variant={
                  category.id ===
                  state.selectedCategoryId
                    ? "primary"
                    : "normal"
                }
                stretch
                onClick={() =>
                  onCategoryChange?.(
                    category.id,
                  )
                }
              />
            ),
          )}
        </div>
      </aside>

      <section className="creative-inventory-panel__catalog">
        <header className="creative-inventory-panel__header">
          <Text
            text="Catalog"
            variant="heading"
          />
          <TextInput
            value={
              state.searchQuery
            }
            placeholder="Search items"
            aria-label="Search creative items"
            onChange={
              (event) =>
                onSearchChange?.(
                  event.target
                    .value,
                )
            }
          />
        </header>

        <div className="creative-inventory-panel__grid">
          {state.items.map(
            (item) => (
              <InventorySlot
                key={
                  item.id +
                  ":" +
                  (item.name ??
                    "")
                }
                item={item}
                onClick={() =>
                  onItemClick?.(
                    item,
                  )
                }
              />
            ),
          )}
        </div>
      </section>
    </Surface>
  );
}
