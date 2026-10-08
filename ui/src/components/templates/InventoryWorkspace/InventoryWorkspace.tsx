import type { ReactNode } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
import "./InventoryWorkspace.css";

export type InventoryWorkspaceProps = {
  creativeAvailable?: boolean;
  creativeVisible?: boolean;
  onViewChange?(creative: boolean): void;
  character: ReactNode;
  crafting: ReactNode;
  inventory: ReactNode;
  station: ReactNode;
  creative: ReactNode;
};

export function InventoryWorkspace({
  creativeAvailable = false,
  creativeVisible = false,
  onViewChange,
  character,
  crafting,
  inventory,
  station,
  creative,
}: InventoryWorkspaceProps) {
  const showCreative = creativeAvailable && creativeVisible;

  return (
    <div className="inventory-workspace">
      {showCreative ? (
        <div className="inventory-workspace__creative">
          {creative}
          <InventoryViewTabs creative onViewChange={onViewChange} />
        </div>
      ) : creativeAvailable ? (
        <div className="inventory-workspace__creative">
          {inventory}
          <InventoryViewTabs creative={false} onViewChange={onViewChange} />
        </div>
      ) : (
        <div className="inventory-workspace__survival">
          <div className="inventory-workspace__character">{character}</div>
          <div className="inventory-workspace__center">
            {crafting}
            {inventory}
          </div>
          <div className="inventory-workspace__station">{station}</div>
        </div>
      )}
    </div>
  );
}

function InventoryViewTabs({
  creative, onViewChange,
}: { creative: boolean; onViewChange?(creative: boolean): void }) {
  const { t } = useLocalization();
  return (
    <nav className="inventory-workspace__tabs" aria-label={t("ui.inventory")}>
      <Button label={t("ui.inventory")} variant={creative ? "normal" : "primary"}
        ariaPressed={!creative} disabled={!onViewChange}
        onClick={() => onViewChange?.(false)} />
      <Button label={t("ui.creative")} variant={creative ? "primary" : "normal"}
        ariaPressed={creative} disabled={!onViewChange}
        onClick={() => onViewChange?.(true)} />
    </nav>
  );
}
