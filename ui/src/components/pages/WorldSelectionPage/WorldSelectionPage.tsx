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
  onLoad?(id: string): void;
  onDelete?(id: string): void;
};

export function WorldSelectionPage({
  worlds,
  status,
  error,
  onBack,
  onCreateWorld,
  onLoad,
  onDelete,
}: WorldSelectionPageProps) {
  const { t } = useLocalization();
  return (
    <ScreenShell
      title={t("ui.worlds")}
      background={<CosmicBackground />}
      footer={
        <>
          <Button
            label={t("ui.back")}
            size="menu"
            className="world-selection__footer-button"
            onClick={onBack}
          />
          <Button
            label={t("ui.newWorld")}
            variant="primary"
            size="menu"
            className="world-selection__footer-button"
            onClick={onCreateWorld}
          />
        </>
      }
    >
      <section className="world-selection">
        {status && worlds.length === 0 && (
          <div className="world-selection__empty">
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
