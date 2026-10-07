import { Button } from "../../atoms/Button/Button";
import { Text } from "../../atoms/Text/Text";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import "./StartingScreenPage.css";

export type StartingScreenPageProps = {
  settingsAvailable?: boolean;
  controlsAvailable?: boolean;
  onPlay(): void;
  onSettings?(): void;
  onControls?(): void;
  onExit(): void;
};

export function StartingScreenPage({
  settingsAvailable = false,
  controlsAvailable = false,
  onPlay,
  onSettings,
  onControls,
  onExit,
}: StartingScreenPageProps) {
  return (
    <main className="starting-screen">
      <CosmicBackground />
      <section className="starting-screen__content">
        <div className="starting-screen__brand">
          <Text
            text="ASTERIA"
            variant="screen-title"
            className="starting-screen__logo"
          />
        </div>

        <Button
          label="Play"
          variant="primary"
          size="menu"
          className="starting-screen__button"
          onClick={onPlay}
        />
        <Button
          label="Settings"
          size="menu"
          className="starting-screen__button"
          disabled={!settingsAvailable}
          onClick={onSettings}
        />
        <Button
          label="Controls"
          size="menu"
          className="starting-screen__button"
          disabled={!controlsAvailable}
          onClick={onControls}
        />
        <Button
          label="Exit Game"
          size="menu"
          className="starting-screen__button"
          onClick={onExit}
        />
      </section>
    </main>
  );
}
