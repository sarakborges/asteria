import { createHudMeter } from "../molecules/HudMeter";
import "./PlayerVitals.css";

export type VitalValue = {
  current: number;
  maximum: number;
};

export type PlayerVitalsState = {
  health?: VitalValue | null;
  stamina?: VitalValue | null;
} | null;

export type PlayerVitalsView = {
  element: HTMLElement;
  setState(state: PlayerVitalsState): void;
};

export function createPlayerVitals(): PlayerVitalsView {
  const root = document.createElement("section");
  root.className = "player-vitals";
  root.hidden = true;

  const health = createHudMeter({
    label: "Health",
    tone: "health",
  });
  const stamina = createHudMeter({
    label: "Stamina",
    tone: "stamina",
  });

  root.append(health.element, stamina.element);

  return {
    element: root,
    setState(state) {
      const hasHealth = Boolean(state?.health);
      const hasStamina = Boolean(state?.stamina);
      root.hidden = !hasHealth && !hasStamina;

      health.element.hidden = !hasHealth;
      stamina.element.hidden = !hasStamina;

      if (state?.health) {
        health.setLabel(
          `Health  ${formatVital(state.health)}`,
        );
        health.setValue(ratio(state.health));
      }

      if (state?.stamina) {
        stamina.setLabel(
          `Stamina  ${formatVital(state.stamina)}`,
        );
        stamina.setValue(ratio(state.stamina));
      }
    },
  };
}

function ratio(value: VitalValue): number {
  if (!Number.isFinite(value.maximum) || value.maximum <= 0) return 0;
  if (!Number.isFinite(value.current)) return 0;
  return value.current / value.maximum;
}

function formatVital(value: VitalValue): string {
  return `${Math.max(0, Math.round(value.current))}/${Math.max(0, Math.round(value.maximum))}`;
}
