import { useMemo, useRef, useState, type PointerEvent } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  GameplayInventoryState, InventoryCatalogEntry, VitalValue,
} from "../../../state/uiState";
import { inventoryItemView, sameInventoryMetadata } from "../../../presentation/inventoryModels";
import { Button } from "../../atoms/Button/Button";
import { Text } from "../../atoms/Text/Text";
import { InventoryCursorOverlay } from "../../molecules/InventoryCursorOverlay/InventoryCursorOverlay";
import { CharacterInfoPanel } from "../../organisms/CharacterInfoPanel/CharacterInfoPanel";
import { CraftingPanel } from "../../organisms/CraftingPanel/CraftingPanel";
import { CreativeInventoryPanel } from "../../organisms/CreativeInventoryPanel/CreativeInventoryPanel";
import { CurrentStationPanel } from "../../organisms/CurrentStationPanel/CurrentStationPanel";
import { PlayerInventoryPanel } from "../../organisms/PlayerInventoryPanel/PlayerInventoryPanel";
import { InventoryWorkspace } from "../../templates/InventoryWorkspace/InventoryWorkspace";
import "./InventoryGameplayPage.css";

export type InventoryGameplayPageProps = {
  state: GameplayInventoryState;
  health?: VitalValue | null;
  onClose(): void;
  onSlotClick(index: number): void;
  onEquipmentClick(index: number): void;
  onSort(): void;
  onDiscardCursor(): void;
  onCreativePick(choice: InventoryCatalogEntry): void;
  onCraft(recipeId: string): void;
  onRotatePortrait(deltaX: number): void;
};

/**
 * In-game inventory and Storybook use the same panel composition.
 * Only Godot-sourced slots and vitals are displayed as gameplay facts.
 */
export function InventoryGameplayPage({
  state, health, onClose, onSlotClick, onEquipmentClick, onSort,
  onDiscardCursor, onCreativePick, onCraft, onRotatePortrait,
}: InventoryGameplayPageProps) {
  const { t, contentName } = useLocalization();
  const cursorRef = useRef<HTMLDivElement>(null);
  const [creativeTab, setCreativeTab] = useState(false);
  const [selectedRecipeId, setSelectedRecipeId] = useState<string | null>(null);
  const [search, setSearch] = useState("");
  const [creativeSearch, setCreativeSearch] = useState("");
  const [category, setCategory] = useState<string | null>(null);
  const categories = useMemo(
    () => [...new Set(state.catalog.map(item => item.category))]
      .sort((a, b) => a < b ? -1 : a > b ? 1 : 0)
      .map(id => ({ id, label: id })),
    [state.catalog],
  );
  const creative = state.creativeAvailable && creativeTab;
  const creativeItems = useMemo(() => {
    const query = creativeSearch.trim().toLowerCase();
    return state.catalog.filter(item =>
      (category === null || item.category === category) &&
      (item.name.toLowerCase().includes(query) || item.id.toLowerCase().includes(query)),
    ).map(item => ({
      ...item,
      quantity: 1,
      name: Object.keys(item.metadata).length > 0
        ? item.name + " (" + Object.values(item.metadata).join(", ") + ")"
        : item.name,
    }));
  }, [state.catalog, category, creativeSearch]);
  const { backpack, hotbar, cursor } = useMemo(() => ({
    backpack: state.backpack.map(slot => inventoryItemView(slot, state.catalog)),
    hotbar: state.hotbar.map(slot => inventoryItemView(slot, state.catalog)),
    cursor: inventoryItemView(state.cursor, state.catalog),
  }), [state.backpack, state.hotbar, state.cursor, state.catalog]);

  const recipes = useMemo(() => state.recipes.map(recipe => {
    const result = state.catalog.find(item =>
      item.id === recipe.resultId && Object.keys(item.metadata).length === 0);
    return {
      id: recipe.id,
      result: {
        id: recipe.resultId,
        kind: result?.kind,
        iconUrl: result?.iconUrl,
      },
      outputQuantity: recipe.outputQuantity,
      ingredients: recipe.ingredients.map(ingredient => {
        const item = state.catalog.find(choice =>
          choice.id === ingredient.id && choice.kind === "item" &&
          Object.keys(choice.metadata).length === 0);
        return {
          item: { id: ingredient.id, kind: "item" as const, iconUrl: item?.iconUrl },
          required: ingredient.required,
          available: ingredient.available,
        };
      }),
      craftable: recipe.craftable,
    };
  }), [state.recipes, state.catalog]);
  const selectedRecipe = recipes.some(recipe => recipe.id === selectedRecipeId)
    ? selectedRecipeId : recipes[0]?.id ?? null;
  const status = state.craftingStatus
    ? state.craftingStatus.code === "Crafted"
      ? t("crafting.crafted", {
        item: contentName(state.recipes.find(
          recipe => recipe.id === state.craftingStatus?.recipeId,
        )?.resultId ?? state.craftingStatus.recipeId),
      })
      : t({
        UnknownRecipe: "crafting.recipeUnavailable",
        MissingIngredients: "crafting.missingIngredients",
        InventoryFull: "crafting.inventoryFull",
      }[state.craftingStatus.code])
    : undefined;

  const followPointer = (event: PointerEvent<HTMLElement>) => {
    const overlay = cursorRef.current;
    if (!overlay) return;
    overlay.style.left = event.clientX - 20 + "px";
    overlay.style.top = event.clientY - 20 + "px";
    overlay.style.visibility = "visible";
  };

  return (
    <main className="inventory-gameplay"
      onPointerMove={followPointer}
      onPointerLeave={() => {
        if (cursorRef.current) cursorRef.current.style.visibility = "hidden";
      }}>
      <div className="inventory-gameplay__actions">
        <Button label={t("inventory.close")} onClick={onClose} />
      </div>
      <div className="inventory-gameplay__workspace">
        <InventoryWorkspace
          creativeAvailable={state.creativeAvailable}
          creativeVisible={creative}
          onViewChange={setCreativeTab}
          character={<CharacterInfoPanel
            onEquipmentClick={onEquipmentClick}
            portraitUrl={state.portraitUrl}
            onRotatePortrait={onRotatePortrait}
            state={health ? {
            name: t("ui.player"),
            healthCurrent: health.current,
            healthMaximum: health.maximum,
            equipment: ["helmet", "chest", "legs", "boots"].map((slot, index) => ({
              slot,
              item: inventoryItemView(state.equipment[index], state.catalog),
              emptyLabel: t(`inventory.equipment.${slot}`),
            })),
          } : null} />}
          crafting={<CraftingPanel
            recipes={recipes} selectedRecipeId={selectedRecipe}
            status={status} onSelectRecipe={setSelectedRecipeId}
            onCraft={onCraft} />}
          inventory={<PlayerInventoryPanel
            state={{ searchQuery: search, backpack, hotbar }}
            onSearchChange={setSearch}
            onSort={onSort}
            onTrash={cursor ? onDiscardCursor : undefined}
            onSlotClick={index => onSlotClick(index)}
          />}
          station={<CurrentStationPanel station={state.recipes.length > 0 ? {
            eyebrow: t("crafting.baseStation"),
            name: t("crafting.personalCrafting"),
            description: t("crafting.available"),
          } : null} />}
          creative={<CreativeInventoryPanel
            state={{
              searchQuery: creativeSearch,
              selectedCategoryId: category,
              categories,
              items: creativeItems,
            }}
            hotbar={hotbar}
            onHotbarSlotClick={index => onSlotClick(index)}
            onTrash={cursor ? onDiscardCursor : undefined}
            onSearchChange={setCreativeSearch}
            onCategoryChange={setCategory}
            onItemClick={item => {
              const choice = state.catalog.find(entry =>
                entry.id === item.id && entry.kind === item.kind &&
                sameInventoryMetadata(entry.metadata, item.metadata ?? {}));
              if (choice) onCreativePick(choice);
            }}
          />}
        />
      </div>
      {cursor && <InventoryCursorOverlay ref={cursorRef} item={cursor} />}
      {state.errorKey && (
        <aside className="inventory-gameplay__cursor-status" role="status">
          <Text text={t(state.errorKey)} variant="caption" />
        </aside>
      )}
    </main>
  );
}
