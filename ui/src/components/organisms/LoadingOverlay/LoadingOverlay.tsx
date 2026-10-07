import type { LoadingState } from "../../../state/uiState";
import { Text } from "../../atoms/Text/Text";
import "./LoadingOverlay.css";

export type LoadingOverlayProps = {
  state: LoadingState | null;
};

export function LoadingOverlay({
  state,
}: LoadingOverlayProps) {
  if (!state) return null;

  const hasProgress = state.total > 0;

  return (
    <section
      className="loading-overlay"
      aria-live="polite"
      aria-busy="true"
    >
      <div className="loading-overlay__card">
        <Text
          text="ASTERIA / LOADING"
          variant="eyebrow"
        />
        <h1 className="loading-overlay__title">
          Preparando Sphere
        </h1>
        <Text
          text={state.phaseLabel}
          variant="detail"
          className="loading-overlay__phase"
        />
        <progress
          className="loading-overlay__progress"
          max={hasProgress ? state.total : undefined}
          value={
            hasProgress
              ? Math.min(
                  state.total,
                  Math.max(0, state.completed),
                )
              : undefined
          }
        />
        <Text
          text={
            hasProgress
              ? Math.max(0, state.completed) +
                " / " +
                state.total
              : ""
          }
          variant="detail"
          className="loading-overlay__counters"
        />
        <Text
          text={state.dimension}
          variant="detail"
          className="loading-overlay__dimension"
        />
      </div>
    </section>
  );
}
