import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
import { Text } from "../../atoms/Text/Text";
import "./DeathScreenPage.css";

export type DeathScreenPageProps = {
  keepInventory: boolean;
  droppedStacks: number;
  dropCapacityExceeded: boolean;
  onRespawn(): void;
};

export function DeathScreenPage({
  keepInventory, droppedStacks, dropCapacityExceeded, onRespawn,
}: DeathScreenPageProps) {
  const { t } = useLocalization();
  return (
    <main className="death-screen" role="main">
      <section className="death-screen__card" aria-labelledby="death-title">
        <Text text={t("death.title")} variant="heading" />
        <p className="death-screen__explanation">
          {dropCapacityExceeded
            ? t("death.dropsUnavailable")
            : keepInventory
              ? t("death.inventoryKept")
              : t("death.itemsDropped", { count: droppedStacks })}
        </p>
        <Button label={t("death.respawn")} stretch onClick={onRespawn} />
      </section>
    </main>
  );
}
