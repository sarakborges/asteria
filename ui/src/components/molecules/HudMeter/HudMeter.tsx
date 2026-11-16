import "./HudMeter.css";

export type HudMeterTone =
  | "health"
  | "stamina"
  | "neutral";

export type HudMeterProps = {
  label: string;
  value?: number;
  tone?: HudMeterTone;
};

export function HudMeter({
  label,
  value = 1,
  tone = "neutral",
}: HudMeterProps) {
  const normalized = Math.min(
    1,
    Math.max(
      0,
      Number.isFinite(value)
        ? value
        : 0,
    ),
  );

  return (
    <section
      className={
        "hud-meter hud-meter--" + tone
      }
      role="meter"
      aria-valuemin={0}
      aria-valuemax={100}
      aria-valuenow={Math.round(
        normalized * 100,
      )}
    >
      <span className="hud-meter__label">
        {label}
      </span>
      <span className="hud-meter__track">
        <span
          className="hud-meter__fill"
          style={{
            width:
              normalized * 100 + "%",
          }}
        />
      </span>
    </section>
  );
}
