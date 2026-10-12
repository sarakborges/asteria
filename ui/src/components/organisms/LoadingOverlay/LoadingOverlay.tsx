import { useLocalization } from "../../../localization/LocalizationProvider";
import type { LoadingState } from "../../../state/uiState";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { CosmicBackground } from "../CosmicBackground/CosmicBackground";
import "./LoadingOverlay.css";

export type LoadingOverlayProps = {
  state: LoadingState | null;
};

export function LoadingOverlay({ state }: LoadingOverlayProps) {
  const { t, contentName } = useLocalization();
  if (!state) return null;

  const hasProgress = state.total > 0;
  const completed = hasProgress
    ? Math.min(state.total, Math.max(0, state.completed))
    : 0;

  return (
    <section
      className="loading-overlay"
      aria-live="polite"
      aria-busy="true"
    >
      <CosmicBackground />
      <Surface variant="frosted" className="loading-overlay__card">
        <Text text={t("loading.asteria")} variant="eyebrow" />
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
          aria-label={t("loading.preparingSphere")}
          max={hasProgress ? state.total : undefined}
          value={hasProgress ? completed : undefined}
        />
        <Text
          text={hasProgress ? completed + " / " + state.total : ""}
          variant="caption"
          className="loading-overlay__counters"
        />
        <Text
          text={contentName(state.dimension)}
          variant="detail"
          className="loading-overlay__dimension"
        />
      </Surface>
    </section>
  );
}
