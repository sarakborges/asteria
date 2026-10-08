import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import "./StartingScreenPage.css";

const logoUrl = new URL(
  "../../../../../packs/default/ui/branding/asteria_logo.svg",
  import.meta.url,
).href;

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
  const { t } = useLocalization();
  return (
    <main className="starting-screen">
      <CosmicBackground />
      <section className="starting-screen__content">
        <div className="starting-screen__brand">
          <img src={logoUrl} alt="Asteria" className="starting-screen__logo" />
        </div>

        <Button
          label={t("starting.play")}
          variant="primary"
          size="menu"
          className="starting-screen__button"
          onClick={onPlay}
        />
        <Button
          label={t("common.settings")}
          size="menu"
          className="starting-screen__button"
          disabled={!settingsAvailable}
          onClick={onSettings}
        />
        <Button
          label={t("starting.controls")}
          size="menu"
          className="starting-screen__button"
          disabled={!controlsAvailable}
          onClick={onControls}
        />
        <Button
          label={t("common.exitGame")}
          size="menu"
          className="starting-screen__button"
          onClick={onExit}
        />
      </section>
    </main>
  );
}
