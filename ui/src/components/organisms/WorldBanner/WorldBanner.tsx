import { useLocalization } from "../../../localization/LocalizationProvider";
import type { WorldBannerState } from "../../../state/uiState";
import { Compass } from "../../molecules/Compass/Compass";
import "./WorldBanner.css";

export type WorldBannerProps = {
  state: WorldBannerState | null;
};

export function WorldBanner({
  state,
}: WorldBannerProps) {
  const { contentName, t } = useLocalization();
  if (!state) return null;

  return (
    <section className="world-banner">
      <div className="world-banner__identity">
        <strong className="world-banner__name">
          {contentName(state.sphere)}
        </strong>
      </div>
      <span className="world-banner__biome">
        {contentName(state.biome)}
      </span>
      <div className="world-banner__coordinates">
        {t("hud.coordinates", { x: Math.floor(state.x), z: Math.floor(state.z), y: Math.floor(state.y) })}
      </div>
      <Compass heading={state.heading} />
    </section>
  );
}
