import "./HudMeter.css";

export type HudMeterTone = "health" | "stamina" | "neutral";

export type HudMeterProps = {
  label: string;
  value?: number;
  tone?: HudMeterTone;
};

export type HudMeterView = {
  element: HTMLElement;
  setValue(value: number): void;
  setLabel(label: string): void;
};

export function createHudMeter(props: HudMeterProps): HudMeterView {
  const root = document.createElement("section");
  root.className = `hud-meter hud-meter--${props.tone ?? "neutral"}`;

  const label = document.createElement("span");
  label.className = "hud-meter__label";

  const track = document.createElement("span");
  track.className = "hud-meter__track";

  const fill = document.createElement("span");
  fill.className = "hud-meter__fill";
  track.append(fill);

  root.append(label, track);

  const view: HudMeterView = {
    element: root,
    setValue(value) {
      const normalized = Math.min(1, Math.max(0, value));
      fill.style.width = `${normalized * 100}%`;
      root.setAttribute("aria-valuenow", String(Math.round(normalized * 100)));
    },
    setLabel(nextLabel) {
      label.textContent = nextLabel;
    },
  };

  root.setAttribute("role", "meter");
  root.setAttribute("aria-valuemin", "0");
  root.setAttribute("aria-valuemax", "100");
  view.setLabel(props.label);
  view.setValue(props.value ?? 1);

  return view;
}
