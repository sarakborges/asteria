import type { GameMode } from "../../../state/uiState";
import { useLocalization } from "../../../localization/LocalizationProvider";
import { ModePicker } from "../ModePicker/ModePicker";
import "./GameModePicker.css";

const GAME_MODES: readonly GameMode[] = ["Survival", "Creative", "Spectator"];

export type GameModePickerProps = {
  value: GameMode;
  disabled?: boolean;
  onChange(mode: GameMode): void;
};

export function GameModePicker({ value, disabled = false, onChange }: GameModePickerProps) {
  const { t } = useLocalization();
  return (
    <ModePicker
      value={value}
      modes={GAME_MODES}
      className="game-mode-picker"
      label={t("settings.gameMode")}
      labelFor={mode => t("settings.gameMode." + mode.toLowerCase())}
      disabled={disabled}
      onChange={onChange}
    />
  );
}
