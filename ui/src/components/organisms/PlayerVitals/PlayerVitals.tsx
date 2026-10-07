import { useLocalization } from "../../../localization/LocalizationProvider";
import type { PlayerVitalsState } from "../../../state/uiState";
import { HudEntityCard } from "../HudEntityCard/HudEntityCard";
import "./PlayerVitals.css";

export type PlayerVitalsProps = {
  state: PlayerVitalsState | null;
};

export function PlayerVitals({
  state,
}: PlayerVitalsProps) {
  const { t } = useLocalization();
  const health = state?.health ?? null;
  if (!health) return null;

  return (
    <section className="player-vitals">
      <HudEntityCard
        entity={{
          name: t("ui.player"),
          health,
        }}
        secondaryVital={
          state?.stamina
            ? {
                label: t("ui.stamina"),
                value: state.stamina,
              }
            : null
        }
      />
    </section>
  );
}
