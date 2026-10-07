import { displayContentName } from "../../../presentation/formatters";
import type { WorldBannerState } from "../../../state/uiState";
import { Compass } from "../../molecules/Compass/Compass";
import "./WorldBanner.css";

export type WorldBannerProps = {
  state: WorldBannerState | null;
};

export function WorldBanner({
  state,
}: WorldBannerProps) {
  if (!state) return null;

  return (
    <section className="world-banner">
      <div className="world-banner__identity">
        <strong className="world-banner__name">
          {displayContentName(state.sphere)}
        </strong>
      </div>
      <span className="world-banner__biome">
        {displayContentName(state.biome)}
      </span>
      <div className="world-banner__coordinates">
        {"X: " +
          Math.floor(state.x) +
          " | Z: " +
          Math.floor(state.z) +
          " | Y: " +
          Math.floor(state.y)}
      </div>
      <Compass heading={state.heading} />
    </section>
  );
}
