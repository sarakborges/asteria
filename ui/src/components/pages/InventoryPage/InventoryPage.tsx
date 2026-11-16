import type {
  CharacterInfoView, CraftingRecipeView, CreativeInventoryView,
  CurrentStationView, PlayerInventoryView,
} from "../../../presentation/inventoryModels";
import { CharacterInfoPanel } from "../../organisms/CharacterInfoPanel/CharacterInfoPanel";
import { CraftingPanel } from "../../organisms/CraftingPanel/CraftingPanel";
import { CreativeInventoryPanel } from "../../organisms/CreativeInventoryPanel/CreativeInventoryPanel";
import { CurrentStationPanel } from "../../organisms/CurrentStationPanel/CurrentStationPanel";
import { PlayerInventoryPanel } from "../../organisms/PlayerInventoryPanel/PlayerInventoryPanel";
import { InventoryWorkspace } from "../../templates/InventoryWorkspace/InventoryWorkspace";
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
  character, inventory, creative, recipes, selectedRecipeId, station, onViewChange,
}: InventoryPageProps) {
  return (
    <main className="inventory-page">
      <div className="inventory-page__backdrop" />
      <InventoryWorkspace
        creativeAvailable={creativeAvailable}
        creativeVisible={creativeVisible}
        onViewChange={onViewChange}
        character={<CharacterInfoPanel state={character} />}
        crafting={<CraftingPanel recipes={recipes} selectedRecipeId={selectedRecipeId} />}
        inventory={<PlayerInventoryPanel state={inventory} />}
        station={<CurrentStationPanel station={station} />}
        creative={<CreativeInventoryPanel state={creative} />}
      />
    </main>
  );
}
