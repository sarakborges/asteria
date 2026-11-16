import { useLocalization } from "../../../localization/LocalizationProvider";
import type { LoadingPhase, LoadingState } from "../../../state/uiState";
import { Surface } from "../../atoms/Surface/Surface";
import { Text } from "../../atoms/Text/Text";
import { CosmicBackground } from "../CosmicBackground/CosmicBackground";
import "./LoadingOverlay.css";

export type LoadingOverlayProps = {
  state: LoadingState | null;
};

type LoadingStageId =
  | "materializing_initial_area"
  | "preparing_presentation";

type LoadingStageStatus = "pending" | "active" | "done";

const LOADING_STAGES: readonly {
  id: LoadingStageId;
  labelKey: string;
  detailKey: string;
}[] = [
  {
    id: "materializing_initial_area",
    labelKey: "loading.stage.materializing_initial_area",
    detailKey: "loading.stage.detail.materializing_initial_area",
  },
  {
    id: "preparing_presentation",
    labelKey: "loading.stage.preparing_presentation",
    detailKey: "loading.stage.detail.preparing_presentation",
  },
];

export function LoadingOverlay({ state }: LoadingOverlayProps) {
  const { t, contentName } = useLocalization();
  if (!state) return null;

  const hasProgress = state.total > 0;
  const completed = hasProgress
    ? Math.min(state.total, Math.max(0, state.completed))
    : 0;
  const hasStandaloneActivity =
    state.phase !== "materializing_initial_area" &&
    state.phase !== "preparing_presentation";

  return (
    <section
      className="loading-overlay"
      aria-live="polite"
      aria-busy="true"
    >
      <CosmicBackground />
      <Surface variant="frosted" className="loading-overlay__card">
        <Text text={t("loading.asteria")} variant="eyebrow" />
        <h1 className="loading-overlay__title">{t("loading.world")}</h1>

        {hasStandaloneActivity && (
          <div className="loading-overlay__activity">
            <Text
              text={t("loading.runtime." + state.phase)}
              variant="detail"
              className="loading-overlay__phase"
            />
            <progress
              className="loading-overlay__progress loading-overlay__progress--indeterminate"
              aria-label={t("loading.runtime." + state.phase)}
            />
          </div>
        )}

        <section className="loading-overlay__stages-section">
          <h2 className="loading-overlay__stages-title">
            {t("loading.stages")}
          </h2>
          <ol className="loading-overlay__stages" aria-label={t("loading.stages")}>
            {LOADING_STAGES.map((stage, index) => {
              const status = getStageStatus(stage.id, state.phase);
              const active = status === "active";

              return (
                <li
                  key={stage.id}
                  className={
                    "loading-overlay__stage loading-overlay__stage--" + status
                  }
                  aria-current={active ? "step" : undefined}
                >
                  <span className="loading-overlay__stage-index">
                    {String(index + 1).padStart(2, "0")}
                  </span>
                  <div className="loading-overlay__stage-content">
                    <div className="loading-overlay__stage-heading">
                      <h3 className="loading-overlay__stage-name">
                        {t(stage.labelKey)}
                      </h3>
                      <span className="loading-overlay__stage-status">
                        {t("loading.status." + status)}
                      </span>
                    </div>
                    <p className="loading-overlay__stage-detail">
                      {t(stage.detailKey)}
                    </p>
                    {active && (
                      <div className="loading-overlay__stage-progress">
                        <progress
                          className="loading-overlay__progress"
                          aria-label={t(stage.labelKey)}
                          max={hasProgress ? state.total : undefined}
                          value={hasProgress ? completed : undefined}
                        />
                        {hasProgress && (
                          <span className="loading-overlay__counters">
                            {completed} / {state.total}
                          </span>
                        )}
                      </div>
                    )}
                  </div>
                </li>
              );
            })}
          </ol>
        </section>

        <div className="loading-overlay__dimension">
          <span className="loading-overlay__dimension-label">
            {t("worldSelection.dimension")}
          </span>
          <strong className="loading-overlay__dimension-name">
            {state.dimension ? contentName(state.dimension) : "—"}
          </strong>
        </div>
      </Surface>
    </section>
  );
}

function getStageStatus(
  stage: LoadingStageId,
  phase: LoadingPhase,
): LoadingStageStatus {
  if (phase === "materializing_initial_area") {
    return stage === "materializing_initial_area" ? "active" : "pending";
  }

  if (phase === "preparing_presentation") {
    return stage === "materializing_initial_area" ? "done" : "active";
  }

  return "pending";
}
