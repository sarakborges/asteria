import { useEffect, useState } from "react";
import { useLocalization } from "../../../localization/LocalizationProvider";
import type { GameMode, SettingsState } from "../../../state/uiState";
import { Select } from "../../atoms/Select/Select";
import { Slider } from "../../atoms/Slider/Slider";
import { Toggle } from "../../atoms/Toggle/Toggle";
import { Text } from "../../atoms/Text/Text";
import { TextInput } from "../../atoms/TextInput/TextInput";
import { SettingRow } from "../../molecules/SettingRow/SettingRow";
import { GameModePicker } from "../../molecules/GameModePicker/GameModePicker";
import { SettingsPage, type SettingsSectionView } from "../SettingsPage/SettingsPage";
import "./SettingsWorkspacePage.css";

export type SettingsWorkspacePageProps = {
  scope: "game" | "world";
  settings: SettingsState;
  onBack(): void;
  onRenderDistance(value: number): void;
  onTargetPosition(value: "Center" | "TopRight" | "Hidden"): void;
  onHideHints(value: boolean): void;
  onGameplayHint(kind: "RotateBlock" | "BreakOrPlaceBlock", value: boolean): void;
  onWorldTicks(value: number): void;
  onGameMode(value: GameMode): void;
};

export function SettingsWorkspacePage({
  scope, settings, onBack, onRenderDistance, onTargetPosition,
  onWorldTicks, onGameMode, onHideHints, onGameplayHint,
}: SettingsWorkspacePageProps) {
  const { t, language, setLanguage, languages } = useLocalization();
  const [selectedId, setSelectedId] = useState(scope === "game" ? "graphics" : "mode");
  const [ticks, setTicks] = useState(String(settings.world?.ticksPerSecond ?? 40));
  useEffect(() => {
    setSelectedId(scope === "game" ? "graphics" : "mode");
  }, [scope]);
  useEffect(() => {
    if (settings.world) setTicks(String(settings.world.ticksPerSecond));
  }, [settings.world?.ticksPerSecond]);

  const sections: SettingsSectionView[] = scope === "game"
    ? [
      {
        id: "graphics", label: t("settings.section.graphics"),
        content: <SettingRow title={t("settings.renderDistance")}
          description={settings.client
            ? t("settings.renderDistance.value", {
              chunks: settings.client.renderDistanceChunks,
              blocks: settings.client.renderDistanceChunks * 16,
            }) : ""}
          control={<Slider value={settings.client?.renderDistanceChunks ?? 4}
            min={4} max={24} ariaLabel={t("settings.renderDistance")}
            disabled={!settings.client}
            onChange={event => onRenderDistance(Number(event.target.value))} />} />,
      },
      {
        id: "hud", label: t("settings.section.hud"),
        content: <div className="settings-workspace__rows">
          <SettingRow title={t("settings.hideHints")}
            description={t("settings.hideHints.description")}
            control={<Toggle ariaLabel={t("settings.hideHints")}
              disabled={!settings.client}
              checked={settings.client?.hud.hideHints ?? false}
              onChange={onHideHints} />} />
          {([
            ["RotateBlock", "rotateBlock"],
            ["BreakOrPlaceBlock", "breakOrPlaceBlock"],
          ] as const).map(([kind, property]) => (
            <SettingRow key={kind}
              title={t("settings.hint." + property)}
              description={t("settings.hint.description")}
              control={<Toggle
                ariaLabel={t("settings.hint." + property)}
                disabled={!settings.client}
                checked={settings.client?.hud.hints[property] ?? true}
                onChange={value => onGameplayHint(kind, value)} />} />
          ))}
          <SettingRow title={t("settings.targetBlockPosition")}
          description={t("settings.targetBlockPosition.description")}
          control={<Select
            ariaLabel={t("settings.targetBlockPosition")}
            disabled={!settings.client}
            value={settings.client?.hud.targetBlockPosition ?? "Center"}
            options={(["Center", "TopRight", "Hidden"] as const).map(value => ({
              value, label: t("settings.targetBlockPosition." +
                (value === "TopRight" ? "topRight" : value.toLowerCase())),
            }))}
            onChange={event => onTargetPosition(
              event.target.value as "Center" | "TopRight" | "Hidden")} />} />
          </div>,
      },
      {
        id: "language", label: t("settings.section.languages"),
        content: <SettingRow title={t("settings.language")}
          control={<Select value={language} ariaLabel={t("settings.language")}
            options={languages.map(value => ({
              value, label: t(value === "portuguese_brazil"
                ? "language.portugueseBrazil" : "language." + value),
            }))}
            onChange={event => setLanguage(event.target.value as typeof language)} />} />,
      },
    ] : [
      {
        id: "mode", label: t("settings.section.worldSettings"),
        content: <SettingRow title={t("settings.gameMode")}
          description={t("settings.gameMode.description")}
          control={<GameModePicker
            value={settings.world?.mode ?? "Survival"}
            disabled={!settings.world}
            onChange={onGameMode}
          />} />,
      },
      {
        id: "rules", label: t("settings.section.gameRules"),
        content: <SettingRow title={t("settings.ticksBySecond")}
          description={t("settings.ticksBySecond.description")}
          control={<TextInput type="number" min={1} step={1}
            aria-label={t("settings.ticksBySecond")} disabled={!settings.world}
            value={ticks}
            onChange={event => setTicks(event.target.value)}
            onBlur={() => {
              const value = Number(ticks);
              if (/^[1-9][0-9]*$/.test(ticks) &&
                  Number.isSafeInteger(value) && value <= 4294967295)
                onWorldTicks(value);
            }}
            onKeyDown={event => {
              if (event.key === "Enter") event.currentTarget.blur();
            }} />} />,
      },
    ];

  return (
    <div className="settings-workspace">
      <SettingsPage sections={sections} selectedId={selectedId}
        onSelect={setSelectedId} onBack={onBack} />
      {settings.errorKey && (
        <Text text={t(settings.errorKey)} variant="caption" />
      )}
    </div>
  );
}
