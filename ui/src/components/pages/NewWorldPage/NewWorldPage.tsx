import { useEffect, useState, type FormEventHandler } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { WorldCreationState, GameMode, WorldGenerationDraft, WorldGenerationMode } from "../../../state/uiState";
import { Button } from "../../atoms/Button/Button";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { TextInput } from "../../atoms/TextInput/TextInput";
import { Slider } from "../../atoms/Slider/Slider";
import { UInt64Input } from "../../atoms/UInt64Input/UInt64Input";
import { Toggle } from "../../atoms/Toggle/Toggle";
import { SettingRow } from "../../molecules/SettingRow/SettingRow";
import { GameModePicker } from "../../molecules/GameModePicker/GameModePicker";
import { NumericStepper } from "../../molecules/NumericStepper/NumericStepper";
import { SpawnBiomeSelect } from "../../molecules/SpawnBiomeSelect/SpawnBiomeSelect";
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
    keepInventory: boolean;
    generation: WorldGenerationDraft;
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
  const [keepInventory, setKeepInventory] = useState(state.keepInventory);
  const [generation, setGeneration] = useState(state.generation);
  const [missingBiome, setMissingBiome] = useState(false);

  useEffect(() => {
    setSeed(state.seed);
  }, [state.seed]);

  if (!state.visible) return null;

  const request = () => {
    if (generation.singleBiome && generation.spawnBiome === null) {
      setMissingBiome(true);
      setSelectedSection("world-generation");
      return;
    }
    onCreate({ seed: seed.trim(), name, mode, ticksPerSecond,
      spawnCreatures, keepInventory, generation });
  };
  const setFlag = (
    key: "spawnStructures" | "singleBiome" | "spawnCaves" | "spawnOceans",
    checked: boolean,
  ) => setGeneration(previous => ({ ...previous, [key]: checked }));
  const setWorldMode = (next: WorldGenerationMode) =>
    setGeneration(previous => ({
      ...previous, mode: next,
      spawnCaves: next === "Normal",
      spawnOceans: next === "Normal",
    }));
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
      id: "world-generation",
      label: t("newWorld.section.worldGeneration"),
      content: (
        <div className="new-world__form-rows">
          <SettingRow title={t("newWorld.worldType")}
            description={t("newWorld.worldType.description")}
            control={<div className="new-world__mode-picker">
              {(["Normal", "Flat", "Void"] as const).map(next => (
                <Button key={next}
                  label={t("newWorld.worldType." + next.toLowerCase())}
                  variant={generation.mode === next ? "primary" : "normal"}
                  ariaPressed={generation.mode === next}
                  disabled={state.pending}
                  onClick={() => setWorldMode(next)} />
              ))}
            </div>} />
          <SettingRow title={t("newWorld.spawnBiome")}
            description={t("newWorld.spawnBiome.description")}
            control={<SpawnBiomeSelect
              value={generation.spawnBiome}
              options={state.spawnBiomes}
              label={t("newWorld.spawnBiome")}
              randomLabel={t("newWorld.spawnBiome.random")}
              searchPlaceholder={t("newWorld.spawnBiome.search")}
              disabled={state.pending}
              requireSelection={generation.singleBiome}
              onChange={selected => {
                setGeneration(previous => ({ ...previous, spawnBiome: selected }));
                setMissingBiome(false);
              }} />} />
          {missingBiome && <span className="new-world__error" role="alert">
            {t("newWorld.singleBiome.required")}
          </span>}
          {!generation.singleBiome && (
            <SettingRow title={t("newWorld.biomeSizeMultiplier")}
              description={t("newWorld.biomeSizeMultiplier.description")}
              control={<div className="new-world__biome-size">
                <Slider ariaLabel={t("newWorld.biomeSizeMultiplier")}
                  value={generation.biomeSizeTenths} min={5} max={50} step={1}
                  disabled={state.pending}
                  onChange={event => setGeneration(previous => ({
                    ...previous, biomeSizeTenths: Number(event.target.value),
                  }))} />
                <TextInput aria-label={t("newWorld.biomeSizeMultiplier") + " ×"}
                  type="number" min="0.5" max="5" step="0.1"
                  value={(generation.biomeSizeTenths / 10).toFixed(1)}
                  disabled={state.pending}
                  onChange={event => {
                    const parsed = Number(event.target.value);
                    if (Number.isFinite(parsed) && parsed >= 0.5 && parsed <= 5) {
                      setGeneration(previous => ({
                        ...previous, biomeSizeTenths: Math.round(parsed * 10),
                      }));
                    }
                  }} />
                <span aria-hidden="true">×</span>
              </div>} />
          )}
          <div className="new-world__form-rows new-world__form-rows--rules">
            {([
              ["spawnStructures", "newWorld.spawnStructures"],
              ["singleBiome", "newWorld.singleBiome"],
              ["spawnCaves", "newWorld.spawnCaves"],
              ["spawnOceans", "newWorld.spawnOceans"],
            ] as const).map(([key, title]) => (
              <SettingRow key={key} title={t(title)}
                description={t(title + ".description")}
                control={<Toggle
                  ariaLabel={t(title)}
                  checked={generation[key]}
                  disabled={state.pending ||
                    (generation.mode === "Void" &&
                      (key === "spawnCaves" || key === "spawnOceans"))}
                  onChange={checked => setFlag(key, checked)} />} />
            ))}
          </div>
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
          <SettingRow title={t("settings.keepInventory")}
            description={t("settings.keepInventory.description")}
            control={<Toggle ariaLabel={t("settings.keepInventory")}
              checked={keepInventory} disabled={state.pending}
              onChange={setKeepInventory} />} />
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
