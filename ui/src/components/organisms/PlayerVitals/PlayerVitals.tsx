import {
  formatVital,
  vitalRatio,
} from "../../../presentation/formatters";
import type { PlayerVitalsState } from "../../../state/uiState";
import { HudMeter } from "../../molecules/HudMeter/HudMeter";
import "./PlayerVitals.css";

export type PlayerVitalsProps = {
  state: PlayerVitalsState | null;
};

export function PlayerVitals({
  state,
}: PlayerVitalsProps) {
  const health = state?.health ?? null;
  const stamina = state?.stamina ?? null;

  if (!health && !stamina) return null;

  return (
    <section className="player-vitals">
      {health && (
        <HudMeter
          label={"Health  " + formatVital(health)}
          value={vitalRatio(health)}
          tone="health"
        />
      )}
      {stamina && (
        <HudMeter
          label={"Stamina  " + formatVital(stamina)}
          value={vitalRatio(stamina)}
          tone="stamina"
        />
      )}
    </section>
  );
}
