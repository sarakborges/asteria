import { useLocalization } from "../../../localization/LocalizationProvider";
import type { BlockPreviewState, TargetHudState } from "../../../state/uiState";
import { ItemGlyph } from "../../atoms/ItemGlyph/ItemGlyph";
import "./TargetHud.css";

export type TargetHudProps = {
  state: TargetHudState | null;
  preview?: BlockPreviewState;
  miningProgress?: number | null;
  artisansKitResolution?: 1 | 2 | 4 | null;
};

export function TargetHud({
  state,
  preview,
  miningProgress = null,
  artisansKitResolution = null,
}: TargetHudProps) {
  const { contentName, t } = useLocalization();
  if (!state) return null;

  return (
    <section className="target-hud">
      <div className="target-hud__slot">
        <ItemGlyph item={{
          id: state.id, blockPreview: preview,
        }} size="recipe" />
      </div>
      <div className="target-hud__copy">
        <strong>{state.kind === "block" || state.kind === "fluid" ? contentName(state.id) : state.name}</strong>
        {state.details.map(
          (detail, index) => (
            <span key={index}>
              {detail}
            </span>
          ),
        )}
        {state.kind === "block" && artisansKitResolution !== null && (
          <span>{t("hud.artisansKit.resolution")}: 1/{8 / artisansKitResolution}</span>
        )}
        {state.kind === "block" &&
          miningProgress !== null && miningProgress > 0 && (
            <progress
              className="target-hud__mining-progress"
              value={miningProgress}
              max={1}
              aria-label={t("hud.miningProgress")}
            />
          )}
      </div>
    </section>
  );
}
