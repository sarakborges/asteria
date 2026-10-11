import { useEffect, useRef } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
import "./SaveFailureExitDialog.css";

export type SaveFailureExitDialogProps = {
  onCancel(): void;
  onConfirm(): void;
};

export function SaveFailureExitDialog({
  onCancel,
  onConfirm,
}: SaveFailureExitDialogProps) {
  const { t } = useLocalization();
  const dialogRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    dialogRef.current?.focus({ preventScroll: true });
  }, []);

  return (
    <div className="save-failure-exit-dialog">
      <section
        ref={dialogRef}
        role="alertdialog"
        aria-modal="true"
        aria-labelledby="save-failure-exit-title"
        aria-describedby="save-failure-exit-message"
        className="save-failure-exit-dialog__panel"
        tabIndex={-1}
        onKeyDown={event => {
          if (event.key === "Escape") {
            event.preventDefault();
            event.stopPropagation();
            onCancel();
            return;
          }
          if (event.key !== "Tab") return;
          const buttons = event.currentTarget.querySelectorAll("button:not(:disabled)");
          const first = buttons.item(0);
          const last = buttons.item(buttons.length - 1);
          if (event.shiftKey &&
              (document.activeElement === first || document.activeElement === dialogRef.current)) {
            event.preventDefault();
            last?.focus();
          } else if (!event.shiftKey && document.activeElement === last) {
            event.preventDefault();
            first?.focus();
          }
        }}
      >
        <h2 id="save-failure-exit-title">{t("exitSaveFailure.title")}</h2>
        <p id="save-failure-exit-message">{t("exitSaveFailure.message")}</p>
        <div className="save-failure-exit-dialog__actions">
          <Button
            variant="primary"
            stretch
            label={t("exitSaveFailure.keepPlaying")}
            onClick={onCancel}
          />
          <Button
            variant="danger"
            stretch
            label={t("exitSaveFailure.exitWithoutSaving")}
            onClick={onConfirm}
          />
        </div>
      </section>
    </div>
  );
}
