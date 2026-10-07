import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  CharacterInfoView,
  CraftingRecipeView,
  CreativeInventoryView,
  CurrentStationView,
  PlayerInventoryView,
} from "../../../presentation/inventoryModels";
import { Button } from "../../atoms/Button/Button";
import { CharacterInfoPanel } from "../../organisms/CharacterInfoPanel/CharacterInfoPanel";
import { CraftingPanel } from "../../organisms/CraftingPanel/CraftingPanel";
import { CreativeInventoryPanel } from "../../organisms/CreativeInventoryPanel/CreativeInventoryPanel";
import { CurrentStationPanel } from "../../organisms/CurrentStationPanel/CurrentStationPanel";
import { PlayerInventoryPanel } from "../../organisms/PlayerInventoryPanel/PlayerInventoryPanel";
import "./InventoryPage.css";

export type InventoryPageProps = {
  creativeAvailable?: boolean;
  creativeVisible?: boolean;
  character: CharacterInfoView;
  inventory: PlayerInventoryView;
  creative: CreativeInventoryView;
  recipes: readonly CraftingRecipeView[];
  selectedRecipeId: string | null;
  station: CurrentStationView;
  onViewChange?(creative: boolean): void;
};

export function InventoryPage({
  creativeAvailable = false,
  creativeVisible = false,
  character,
  inventory,
  creative,
  recipes,
  selectedRecipeId,
  station,
  onViewChange,
}: InventoryPageProps) {
  return (
    <main className="inventory-page">
      <div className="inventory-page__backdrop" />

      {creativeAvailable &&
      creativeVisible ? (
        <div className="inventory-page__creative-layout">
          <CreativeInventoryPanel
            state={creative}
          />
          <InventoryViewTabs
            creative
            onViewChange={
              onViewChange
            }
          />
        </div>
      ) : (
        <div className="inventory-page__survival-layout">
          <div className="inventory-page__character">
            <CharacterInfoPanel
              state={character}
            />
          </div>

          <div className="inventory-page__center-column">
            <div className="inventory-page__inventory-row">
              <PlayerInventoryPanel
                state={inventory}
              />
              {creativeAvailable && (
                <InventoryViewTabs
                  creative={false}
                  onViewChange={
                    onViewChange
                  }
                />
              )}
            </div>

            <CraftingPanel
              recipes={recipes}
              selectedRecipeId={
                selectedRecipeId
              }
            />
          </div>

          <div className="inventory-page__station">
            <CurrentStationPanel
              station={station}
            />
          </div>
        </div>
      )}
    </main>
  );
}

function InventoryViewTabs({
  creative,
  onViewChange,
}: {
  creative: boolean;
  onViewChange?(
    creative: boolean,
  ): void;
}) {
  const { t } = useLocalization();
  return (
    <aside className="inventory-page__tabs">
      <Button
        label={t("ui.inventory")}
        variant={
          creative
            ? "normal"
            : "primary"
        }
        onClick={() =>
          onViewChange?.(
            false,
          )
        }
      />
      <Button
        label={t("ui.creative")}
        variant={
          creative
            ? "primary"
            : "normal"
        }
        onClick={() =>
          onViewChange?.(
            true,
          )
        }
      />
    </aside>
  );
}
