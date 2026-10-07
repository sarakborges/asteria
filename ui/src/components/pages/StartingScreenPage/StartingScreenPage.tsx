import { useLocalization } from "../../../localization/LocalizationProvider";
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
  const { t, language, setLanguage, languages } = useLocalization();
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

        <label className="starting-screen__language" style={{display:"flex",justifyContent:"center",gap:8,alignItems:"center"}}>
          <span>{t("starting.language")}</span>
          <select aria-label={t("starting.language")} value={language} onChange={event => setLanguage(event.target.value as typeof language)}>
            {languages.map(option => <option key={option} value={option}>{t(option === "portuguese_brazil" ? "language.portugueseBrazil" : "language." + option)}</option>)}
          </select>
        </label>
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
