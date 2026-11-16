import { useLocalization } from "../../../localization/LocalizationProvider";
import type { PlayerVitalsState } from "../../../state/uiState";
import { HudEntityCard } from "../HudEntityCard/HudEntityCard";
import "./PlayerVitals.css";

export type PlayerVitalsProps = {
  state: PlayerVitalsState | null;
  portraitUrl?: string | null;
};

export function PlayerVitals({
  state, portraitUrl,
}: PlayerVitalsProps) {
  const { t } = useLocalization();
  const health = state?.health ?? null;
  if (!health) return null;

  return (
    <section className="player-vitals">
      <HudEntityCard
        entity={{
          name: t("ui.player"),
          health, portraitUrl: portraitUrl ?? undefined,
        }}
        secondaryVital={
          state?.oxygen
            ? {
                label: t("ui.oxygen"),
                value: state.oxygen,
                tone: "oxygen",
              }
            : state?.stamina
              ? {
                  label: t("ui.stamina"),
                  value: state.stamina,
                  tone: "stamina",
                }
              : null
        }
      />
    </section>
  );
}
