import { useLocalization } from "../../../localization/LocalizationProvider";
import type { BlockPreviewState, TargetHudState } from "../../../state/uiState";
import { ItemGlyph } from "../../atoms/ItemGlyph/ItemGlyph";
import "./TargetHud.css";

export type TargetHudProps = {
  state: TargetHudState | null;
  preview?: BlockPreviewState;
  activeLayerIconUrl?: string;
  miningProgress?: number | null;
  artisansKitResolution?: 1 | 2 | 4 | null;
};

export function TargetHud({
  state,
  preview,
  activeLayerIconUrl,
  miningProgress = null,
  artisansKitResolution = null,
}: TargetHudProps) {
  const { contentName, t } = useLocalization();
  if (!state) return null;

  const layerGroups = new Map<string, string[]>();
  for (const layer of state.layers ?? []) {
    const faces = layerGroups.get(layer.id) ?? [];
    if (!faces.includes(layer.face)) faces.push(layer.face);
    layerGroups.set(layer.id, faces);
  }

  return (
    <section className="target-hud">
      {state.kind !== "fluid" && (
      <div className="target-hud__slot">
        <ItemGlyph item={{
          id: state.kind === "block" && state.activeLayerId
            ? state.activeLayerId : state.id,
          blockPreview: state.kind === "object" ||
            (state.kind === "block" && state.activeLayerId && activeLayerIconUrl)
            ? undefined : preview,
          iconUrl: state.kind === "block" && state.activeLayerId
            ? activeLayerIconUrl : state.kind === "object" ? state.iconUrl : undefined,
        }} size="recipe" />
      </div>
      )}
      <div className="target-hud__copy">
        <strong>{state.kind === "block" && state.activeLayerId
          ? contentName(state.activeLayerId)
          : state.kind === "block" || state.kind === "fluid" || state.kind === "object"
            ? contentName(state.id)
            : state.name}</strong>
        {state.kind === "block" && state.activeLayerId && (
          <span>{contentName(state.id)}</span>
        )}
        {state.face && (
          <span>{t("hud.face")}: {t(`hud.layerFace.${state.face}`)}</span>
        )}
        {state.fluidLevel && (
          <span>{t("hud.level")}: {state.fluidLevel.value}/{state.fluidLevel.maximum}</span>
        )}
        {state.skyLight !== undefined && state.blockLight !== undefined && (
          <span>{t("hud.skyLight")}: {state.skyLight} | {t("hud.blockLight")}: {state.blockLight}</span>
        )}
        {state.position && (
          <span>{t("hud.coordinates", state.position)}</span>
        )}
        {state.kind === "block" && layerGroups.size > 0 && (
          <div className="target-hud__layers">
            <span>{t("hud.layers")}:</span>
            {Array.from(layerGroups, ([id, faces]) => (
              <span key={id}>
                {contentName(id)}: {faces.map(face =>
                  t(`hud.layerFace.${face}`)).join(", ")}
              </span>
            ))}
          </div>
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
