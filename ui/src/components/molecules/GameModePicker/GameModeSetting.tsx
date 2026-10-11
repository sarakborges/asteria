import { useLocalization } from "../../../localization/LocalizationProvider";
import { SettingRow } from "../SettingRow/SettingRow";
import { GameModePicker, type GameModePickerProps } from "./GameModePicker";

/** Shared label, layout and equal-width controls for both world settings flows. */
export function GameModeSetting(props: GameModePickerProps) {
  const { t } = useLocalization();
  return (
    <div className="game-mode-setting">
      <SettingRow
        title={t("settings.gameMode")}
        description={t("settings.gameMode.description")}
        control={<GameModePicker {...props} />}
      />
    </div>
  );
}
