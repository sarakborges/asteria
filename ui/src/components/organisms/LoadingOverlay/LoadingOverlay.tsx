import { useLocalization } from "../../../localization/LocalizationProvider";
import type { LoadingState } from "../../../state/uiState";
import { Text } from "../../atoms/Text/Text";
import "./LoadingOverlay.css";

export type LoadingOverlayProps = {
  state: LoadingState | null;
};

export function LoadingOverlay({
  state,
}: LoadingOverlayProps) {
  const { t, contentName } = useLocalization();
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
          text={t("loading.asteria")}
          variant="eyebrow"
        />
        <h1 className="loading-overlay__title">
          {t("loading.preparingSphere")}
        </h1>
        <Text
          text={t("loading.runtime." + state.phase)}
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
          text={contentName(state.dimension)}
          variant="detail"
          className="loading-overlay__dimension"
        />
      </div>
    </section>
  );
}
