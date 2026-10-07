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
  return (
    <main className="pause-menu">
      <section className="pause-menu__actions">
        <Button
          label="Resume"
          stretch
          onClick={onResume}
        />

        <div className="pause-menu__settings-row">
          <Button
            label="World Settings"
            stretch
            disabled={
              !worldSettingsAvailable
            }
            onClick={
              onWorldSettings
            }
          />
          <Button
            label="Game Settings"
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
          label="Controls"
          stretch
          disabled={
            !controlsAvailable
          }
          onClick={onControls}
        />

        <Button
          label="Leave World"
          stretch
          disabled={
            !onLeaveWorld
          }
          onClick={onLeaveWorld}
        />

        <Button
          label="Exit Game"
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
