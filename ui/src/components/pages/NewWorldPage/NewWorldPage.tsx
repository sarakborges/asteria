import { useEffect, useState, type FormEventHandler } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { WorldCreationState, GameMode } from "../../../state/uiState";
import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { TextInput } from "../../atoms/TextInput/TextInput";
import { UInt64Input } from "../../atoms/UInt64Input/UInt64Input";
import { Toggle } from "../../atoms/Toggle/Toggle";
import { SettingRow } from "../../molecules/SettingRow/SettingRow";
import { GameModePicker } from "../../molecules/GameModePicker/GameModePicker";
import { NumericStepper } from "../../molecules/NumericStepper/NumericStepper";
import { CosmicBackground } from "../../organisms/CosmicBackground/CosmicBackground";
import { ScreenShell } from "../../templates/ScreenShell/ScreenShell";
import { SettingsPage, type SettingsSectionView } from "../SettingsPage/SettingsPage";
import "./NewWorldPage.css";

const FORM_ID = "world-creation-form";

export type NewWorldPageProps = {
  state: WorldCreationState;
  onBack(): void;
  onMainMenu?(): void;
  onCreate(request: {
    seed: string; name: string; mode: GameMode; ticksPerSecond: string;
    spawnCreatures: boolean;
  }): void;
  onRandomize(): void;
};

export function NewWorldPage({
  state, onBack, onMainMenu, onCreate, onRandomize,
}: NewWorldPageProps) {
  const { t } = useLocalization();
  const [selectedSection, setSelectedSection] = useState("world-settings");
  const [seed, setSeed] = useState(state.seed);
  const [name, setName] = useState(state.name);
  const [mode, setMode] = useState(state.mode);
  const [ticksPerSecond, setTicksPerSecond] = useState(state.ticksPerSecond);
  const [spawnCreatures, setSpawnCreatures] = useState(state.spawnCreatures);

  useEffect(() => {
    setSeed(state.seed);
  }, [state.seed]);

  if (!state.visible) return null;

  const request = () => onCreate({ seed: seed.trim(), name, mode, ticksPerSecond, spawnCreatures });
  const submit: FormEventHandler<HTMLFormElement> = event => {
    event.preventDefault();
    if (!state.pending) request();
  };

  if (state.generating) {
    return (
      <ScreenShell title={t("newWorld.generating")} background={<CosmicBackground />}
        className="new-world new-world--generating">
        <div className="new-world__generating-content">
          <Surface variant="frosted" className="new-world__generating-panel">
            <Text text={t("newWorld.preparing")} variant="body" />
          </Surface>
        </div>
      </ScreenShell>
    );
  }

  const sections: SettingsSectionView[] = [
    {
      id: "world-settings",
      label: t("settings.section.worldSettings"),
      content: (
        <div className="new-world__form-rows">
          <SettingRow title={t("newWorld.name")}
            description={t("newWorld.name.description")}
            control={<TextInput aria-label={t("newWorld.name")}
              value={name} disabled={state.pending} maxLength={200}
              onChange={event => setName(event.target.value)} />} />
          <div className="new-world__setting">
            <Text text={t("newWorld.seedLabel")} variant="setting-title" />
            <Text text={t("newWorld.seed.description")} variant="caption" />
            <div className="new-world__seed-row">
              <UInt64Input id="world-seed" ariaLabel={t("newWorld.seed")}
                descriptionId="world-seed-help" placeholder={t("newWorld.seedPlaceholder")}
                value={seed} disabled={state.pending}
                invalid={state.errorKey === "newWorld.error.invalidSeed"}
                onChange={setSeed} />
              <Button label={t("newWorld.randomSeed")} disabled={state.pending}
                onClick={onRandomize} className="new-world__randomize" />
            </div>
            <span id="world-seed-help" className="new-world__help">
              {t("newWorld.seedRange")}
            </span>
            {state.errorKey && (
              <span className="new-world__error" role="alert">
                {t(state.errorKey)}
              </span>
            )}
          </div>
          <SettingRow title={t("settings.gameMode")}
            description={t("settings.gameMode.description")}
            control={<GameModePicker value={mode} disabled={state.pending}
              onChange={setMode} />} />
        </div>
      ),
    },
    {
      id: "game-rules",
      label: t("settings.section.gameRules"),
      content: (
        <div className="new-world__form-rows new-world__form-rows--rules">
          <SettingRow title={t("settings.ticksBySecond")}
            description={t("settings.ticksBySecond.description")}
            control={<NumericStepper
              ariaLabel={t("settings.ticksBySecond")}
              min={1} max={4294967295}
              value={ticksPerSecond} disabled={state.pending}
              onChange={setTicksPerSecond}
            />} />
          <SettingRow title={t("settings.spawnCreatures")}
            description={t("settings.spawnCreatures.description")}
            control={<Toggle ariaLabel={t("settings.spawnCreatures")}
              checked={spawnCreatures} disabled={state.pending}
              onChange={setSpawnCreatures} />} />
        </div>
      ),
    },
  ];

  const footer = (
    <>
      {onMainMenu && (
        <Button label={t("newWorld.backToMenu")} size="menu"
          className="new-world__create" disabled={state.pending}
          onClick={onMainMenu} />
      )}
      <Button label={t("newWorld.backToWorlds")} size="menu"
        className="new-world__create" disabled={state.pending}
        onClick={onBack} />
      <Button label={t("newWorld.createWorld")}
        variant="primary" size="menu" type="submit"
        formId={FORM_ID} className="new-world__create"
        disabled={state.pending} />
    </>
  );

  return (
    <SettingsPage
      className="new-world"
      title={t("newWorld.title")}
      sections={sections}
      selectedId={selectedSection}
      onSelect={setSelectedSection}
      onBack={onBack}
      footer={footer}
      formId={FORM_ID}
      onSubmit={submit}
    />
  );
}
