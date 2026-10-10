import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
import "./DeathScreenPage.css";

export type DeathScreenPageProps = {
  keepInventory: boolean;
  droppedStacks: number;
  dropCapacityExceeded: boolean;
  outcomeKnown: boolean;
  saveError?: boolean;
  onRespawn(): void;
  onSaveAndLeave(): void;
};

export function DeathScreenPage({
  keepInventory, droppedStacks, dropCapacityExceeded,
  outcomeKnown, saveError = false, onRespawn, onSaveAndLeave,
}: DeathScreenPageProps) {
  const { t } = useLocalization();
  return (
    <main className="death-screen" role="main">
      <section className="death-screen__card" aria-labelledby="death-title">
        <h1 id="death-title" className="death-screen__title">{t("death.title")}</h1>
        <p className="death-screen__explanation">
          {!outcomeKnown
            ? t("death.restored")
            : dropCapacityExceeded
              ? t("death.dropsUnavailable")
            : keepInventory
              ? t("death.inventoryKept")
              : t("death.itemsDropped", { count: droppedStacks })}
        </p>
        <Button label={t("death.respawn")} stretch onClick={onRespawn} />
        <Button label={t("ui.leaveWorld")} stretch onClick={onSaveAndLeave} />
        {saveError && (
          <p role="alert" className="death-screen__error">
            {t("ui.worldSaveFailed")}
          </p>
        )}
      </section>
    </main>
  );
}
