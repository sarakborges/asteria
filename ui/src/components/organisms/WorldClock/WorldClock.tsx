import { useLocalization } from "../../../localization/LocalizationProvider";
import type { WorldClockState } from "../../../state/uiState";
import "./WorldClock.css";

export type WorldClockProps = {
  state: WorldClockState | null;
};

export function WorldClock({
  state,
}: WorldClockProps) {
  const { t } = useLocalization();
  if (!state) return null;

  return (
    <div className="world-clock">
      <span>
        {t("ui.day", { day: state.day })}
      </span>
      <span>
        {String(
          state.hour,
        ).padStart(2, "0")}
        :
        {String(
          state.minute,
        ).padStart(2, "0")}
      </span>
    </div>
  );
}
