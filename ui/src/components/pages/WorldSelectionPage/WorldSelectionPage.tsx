import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import {
  WorldCard,
  type WorldSummaryView,
} from "../../organisms/WorldCard/WorldCard";
import { ScreenShell } from "../../templates/ScreenShell/ScreenShell";
import "./WorldSelectionPage.css";

export type WorldSelectionPageProps = {
  worlds: readonly WorldSummaryView[];
  status?: string;
  error?: string;
  onBack(): void;
  onCreateWorld(): void;
  onOpenSavesFolder?(): void;
  onLoad?(id: string): void;
  onDelete?(id: string): void;
};

export function WorldSelectionPage({
  worlds,
  status,
  error,
  onBack,
  onCreateWorld,
  onOpenSavesFolder,
  onLoad,
  onDelete,
}: WorldSelectionPageProps) {
  const { t } = useLocalization();
  return (
    <ScreenShell
      title={t("starting.loadWorlds")}
      className="world-selection-screen"
      background={<CosmicBackground />}
      footer={
        <>
          <Button
            label={t("newWorld.return")}
            className="world-selection__footer-button"
            onClick={onBack}
          />
          <Button
            label={t("worldSelection.openSavesFolder")}
            className="world-selection__footer-button"
            disabled={!onOpenSavesFolder}
            onClick={onOpenSavesFolder}
          />
          <Button
            label={t("starting.newWorld")}
            variant="primary"
            className="world-selection__footer-button"
            onClick={onCreateWorld}
          />
        </>
      }
    >
      <section className="world-selection">
        {status && worlds.length === 0 && (
          <div className="world-selection__empty" role="status">
            {status}
          </div>
        )}

        {worlds.map((world) => (
          <WorldCard
            key={world.id}
            world={world}
            onLoad={onLoad}
            onDelete={onDelete}
          />
        ))}

        {error && (
          <div
            className="world-selection__error"
            role="alert"
          >
            {error}
          </div>
        )}
      </section>
    </ScreenShell>
  );
}
