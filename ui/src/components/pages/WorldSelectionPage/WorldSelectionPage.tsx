import { Fragment, useState } from "react";
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
  busy?: boolean;
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
  busy = false,
}: WorldSelectionPageProps) {
  const { t } = useLocalization();
  const [confirmId, setConfirmId] = useState<string | null>(null);
  const confirmedWorld = worlds.find(world => world.id === confirmId);
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
          <Fragment key={world.id}>
            <WorldCard
              world={world}
              onLoad={busy ? undefined : onLoad}
              onDelete={busy ? undefined : setConfirmId}
            />
            {confirmedWorld?.id === world.id && onDelete && !busy && (
              <div className="world-selection__confirmation"
                role="alertdialog" aria-label={t("worldSelection.delete")}
                onKeyDown={event => {
                  if (event.key === "Escape") {
                    setConfirmId(null);
                    event.preventDefault();
                    event.stopPropagation();
                  }
                }}>
                <p>{t("worldSelection.confirmDelete", { name: world.id })}</p>
                <div className="world-selection__confirmation-actions">
                  <Button label={t("ui.back")} onClick={() => setConfirmId(null)} />
                  <Button label={t("ui.delete")} variant="danger" onClick={() => {
                    onDelete(world.id);
                    setConfirmId(null);
                  }} />
                </div>
              </div>
            )}
          </Fragment>
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
