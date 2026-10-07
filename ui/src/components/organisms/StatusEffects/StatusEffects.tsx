import type { StatusEffectState } from "../../../state/uiState";
import "./StatusEffects.css";

export type StatusEffectsProps = {
  effects: readonly StatusEffectState[];
};

export function StatusEffects({
  effects,
}: StatusEffectsProps) {
  if (effects.length === 0) return null;

  return (
    <aside className="status-effects">
      {effects.slice(0, 8).map((effect) => (
        <div
          key={effect.id}
          className={
            "status-effects__item status-effects__item--" +
            effect.tone
          }
          data-effect-id={effect.id}
        >
          <span
            className="status-effects__marker"
            aria-hidden="true"
          />
          <span className="status-effects__copy">
            <strong>{effect.label}</strong>
            <span className="status-effects__duration">
              {effect.duration ?? ""}
            </span>
          </span>
        </div>
      ))}
    </aside>
  );
}
