import type { PlayerVitalsState } from "../../../state/uiState";
import { HudEntityCard } from "../HudEntityCard/HudEntityCard";
import "./PlayerVitals.css";

export type PlayerVitalsProps = {
  state: PlayerVitalsState | null;
};

export function PlayerVitals({
  state,
}: PlayerVitalsProps) {
  const health = state?.health ?? null;
  if (!health) return null;

  return (
    <section className="player-vitals">
      <HudEntityCard
        entity={{
          name: "Player",
          health,
        }}
        secondaryVital={
          state?.stamina
            ? {
                label: "Stamina",
                value: state.stamina,
              }
            : null
        }
      />
    </section>
  );
}
