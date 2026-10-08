import type { GameMode } from "../../../state/uiState";
import { useLocalization } from "../../../localization/LocalizationProvider";
import { Button } from "../../atoms/Button/Button";
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
    <div className="game-mode-picker" role="group" aria-label={t("settings.gameMode")}>
      {GAME_MODES.map(mode => (
        <Button
          key={mode}
          label={t("settings.gameMode." + mode.toLowerCase())}
          variant={mode === value ? "primary" : "normal"}
          ariaPressed={mode === value}
          disabled={disabled}
          onClick={() => onChange(mode)}
        />
      ))}
    </div>
  );
}
