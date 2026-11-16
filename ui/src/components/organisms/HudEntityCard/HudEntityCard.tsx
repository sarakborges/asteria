import { useLocalization } from "../../../localization/LocalizationProvider";
import type {
  HudEntityState,
  VitalValue,
} from "../../../state/uiState";
import "./HudEntityCard.css";

export type HudEntityCardProps = {
  entity: HudEntityState;
  secondaryVital?: {
    label: string;
    value: VitalValue;
    tone?: "stamina" | "oxygen";
  } | null;
};

export function HudEntityCard({
  entity,
  secondaryVital = null,
}: HudEntityCardProps) {
  const { contentName } = useLocalization();
  const name = entity.name.startsWith("asteria:")
    ? contentName(entity.name)
    : entity.name;

  return (
    <section className="hud-entity-card">
      <div className="hud-entity-card__avatar">
        {entity.portraitUrl ? (
          <img
            src={entity.portraitUrl}
            alt=""
          />
        ) : (
          <span aria-hidden="true">?</span>
        )}
      </div>

      <div className="hud-entity-card__info">
        <strong className="hud-entity-card__name">
          {name}
        </strong>
        <VitalBar
          value={entity.health}
          tone="health"
        />
        {secondaryVital && (
          <VitalBar
            value={secondaryVital.value}
            tone={secondaryVital.tone ?? "stamina"}
            label={secondaryVital.label}
          />
        )}
      </div>
    </section>
  );
}

function VitalBar({
  value,
  tone,
  label,
}: {
  value: VitalValue;
  tone: "health" | "stamina" | "oxygen";
  label?: string;
}) {
  const ratio =
    value.maximum > 0
      ? Math.min(
          1,
          Math.max(
            0,
            value.current /
              value.maximum,
          ),
        )
      : 0;

  return (
    <div
      className={
        "hud-entity-card__health hud-entity-card__health--" +
        tone
      }
      role="meter"
      aria-valuemin={0}
      aria-valuemax={value.maximum}
      aria-valuenow={value.current}
    >
      <span
        className="hud-entity-card__health-fill"
        style={{
          width:
            ratio * 100 + "%",
        }}
      />
      <span className="hud-entity-card__health-label">
        {label ? label + " " : ""}
        {Math.round(
          value.current,
        )}{" "}
        /{" "}
        {Math.round(
          value.maximum,
        )}
      </span>
    </div>
  );
}
