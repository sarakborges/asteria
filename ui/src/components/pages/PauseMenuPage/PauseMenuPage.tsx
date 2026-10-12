import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
import { Text } from "../../atoms/Text/Text";
import "./PauseMenuPage.css";

export type PauseMenuPageProps = {
  worldSettingsAvailable?: boolean;
  gameSettingsAvailable?: boolean;
  controlsAvailable?: boolean;
  saveFeedback?: string;
  onResume(): void;
  onWorldSettings?(): void;
  onGameSettings?(): void;
  onControls?(): void;
  onLeaveWorld?(): void;
  onExitGame(): void;
};

export function PauseMenuPage({
  worldSettingsAvailable = false,
  gameSettingsAvailable = false,
  controlsAvailable = false,
  saveFeedback = "",
  onResume,
  onWorldSettings,
  onGameSettings,
  onControls,
  onLeaveWorld,
  onExitGame,
}: PauseMenuPageProps) {
  const { t } = useLocalization();
  return (
    <main className="pause-menu">
      <section className="pause-menu__actions">
        <Button
          label={t("ui.resume")}
          stretch
          onClick={onResume}
        />

        <div className="pause-menu__settings-row">
          <Button
            label={t("ui.worldSettings")}
            stretch
            disabled={
              !worldSettingsAvailable
            }
            onClick={
              onWorldSettings
            }
          />
          <Button
            label={t("common.gameSettings")}
            stretch
            disabled={
              !gameSettingsAvailable
            }
            onClick={
              onGameSettings
            }
          />
        </div>

        <Button
          label={t("common.controls")}
          stretch
          disabled={
            !controlsAvailable
          }
          onClick={onControls}
        />

        <Button
          label={t("ui.leaveWorld")}
          stretch
          disabled={
            !onLeaveWorld
          }
          onClick={onLeaveWorld}
        />

        <Button
          label={t("common.exitGame")}
          variant="danger"
          stretch
          onClick={onExitGame}
        />

        <Text
          text={saveFeedback}
          variant="caption"
          className="pause-menu__feedback"
        />
      </section>
    </main>
  );
}
