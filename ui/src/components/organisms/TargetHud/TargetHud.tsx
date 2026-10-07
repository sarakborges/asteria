import type { TargetHudState } from "../../../state/uiState";
import { abbreviateContentId } from "../../../presentation/formatters";
import "./TargetHud.css";

export type TargetHudProps = {
  state: TargetHudState | null;
};

export function TargetHud({
  state,
}: TargetHudProps) {
  if (!state) return null;

  return (
    <section className="target-hud">
      <div className="target-hud__slot">
        <span aria-hidden="true">
          {abbreviateContentId(
            state.id,
          )}
        </span>
      </div>
      <div className="target-hud__copy">
        <strong>{state.name}</strong>
        {state.details.map(
          (detail, index) => (
            <span key={index}>
              {detail}
            </span>
          ),
        )}
      </div>
    </section>
  );
}
