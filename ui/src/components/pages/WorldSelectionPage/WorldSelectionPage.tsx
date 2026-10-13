import { Fragment, useEffect, useRef, useState } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import {
  WorldCard,
  type WorldSummaryView,
} from "../../organisms/WorldCard/WorldCard";
import { ScreenShell } from "../../templates/ScreenShell/ScreenShell";
import { ScrollArea } from "../../molecules/ScrollArea/ScrollArea";
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
  onRecreate?(id: string): void;
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
  onRecreate,
  busy = false,
}: WorldSelectionPageProps) {
  const { t } = useLocalization();
  const [confirmId, setConfirmId] = useState<string | null>(null);
  const dialogRef = useRef<HTMLDivElement | null>(null);
  const returnFocus = useRef<HTMLElement | null>(null);
  const confirmedWorld = worlds.find(world => world.id === confirmId);
  const openConfirmation = (id: string) => {
    returnFocus.current = document.activeElement instanceof HTMLElement
      ? document.activeElement : null;
    setConfirmId(id);
  };
  const closeConfirmation = () => {
    setConfirmId(null);
    returnFocus.current?.focus();
    returnFocus.current = null;
  };
  useEffect(() => {
    if (confirmedWorld && !busy)
      dialogRef.current?.querySelector<HTMLButtonElement>("button")?.focus();
    if (busy || !confirmedWorld) setConfirmId(null);
  }, [confirmId, confirmedWorld, busy]);
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
      <ScrollArea className="world-selection-scroll"
        viewportClassName="world-selection"
        contentClassName="world-selection__content"
        ariaLabel={t("starting.loadWorlds")}>
        {status && worlds.length === 0 && (
          <div className="world-selection__empty" role="status">
            {status}
          </div>
        )}

        {worlds.map((world) => (
          <Fragment key={world.id}>
            <WorldCard
              world={world}
              onLoad={busy || confirmedWorld ? undefined : onLoad}
              onDelete={busy || confirmedWorld ? undefined : openConfirmation}
              onRecreate={busy || confirmedWorld ? undefined : onRecreate}
            />
            {confirmedWorld?.id === world.id && onDelete && !busy && (
              <div ref={dialogRef} className="world-selection__confirmation"
                role="alertdialog" aria-modal="true"
                aria-label={t("worldSelection.delete")}
                onKeyDown={event => {
                  if (event.key === "Escape") {
                    closeConfirmation();
                    event.preventDefault();
                    event.stopPropagation();
                  } else if (event.key === "Tab") {
                    const buttons = dialogRef.current?.querySelectorAll("button");
                    if (!buttons?.length) return;
                    const first = buttons[0];
                    const last = buttons[buttons.length - 1];
                    if (event.shiftKey && document.activeElement === first) {
                      last.focus();
                      event.preventDefault();
                    } else if (!event.shiftKey && document.activeElement === last) {
                      first.focus();
                      event.preventDefault();
                    }
                  }
                }}>
                <p>{t("worldSelection.confirmDelete", { name: world.id })}</p>
                <div className="world-selection__confirmation-actions">
                  <Button label={t("ui.back")} onClick={closeConfirmation} />
                  <Button label={t("ui.delete")} variant="danger" onClick={() => {
                    onDelete(world.id);
                    setConfirmId(null);
                    returnFocus.current = null;
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
      </ScrollArea>
    </ScreenShell>
  );
}
