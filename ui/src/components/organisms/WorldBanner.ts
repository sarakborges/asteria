import {
  createCompass,
  type CompassView,
} from "../molecules/Compass";
import "./WorldBanner.css";

export type WorldBannerState = {
  sphere: string;
  x: number;
  y: number;
  z: number;
  heading: number;
};

export type WorldBannerView = {
  element: HTMLElement;
  compass: CompassView;
  setState(state: WorldBannerState | null): void;
};

export function createWorldBanner(): WorldBannerView {
  const root = document.createElement("section");
  root.className = "world-banner";
  root.hidden = true;

  const identity = document.createElement("div");
  identity.className = "world-banner__identity";

  const kind = document.createElement("span");
  kind.className = "world-banner__kind";
  kind.textContent = "Sphere";

  const name = document.createElement("strong");
  name.className = "world-banner__name";

  identity.append(kind, name);

  const coordinates = document.createElement("div");
  coordinates.className = "world-banner__coordinates";

  const compass = createCompass();

  root.append(identity, coordinates, compass.element);

  return {
    element: root,
    compass,
    setState(state) {
      root.hidden = state === null;
      if (state === null) {
        name.textContent = "";
        coordinates.textContent = "";
        compass.setHeading(0);
        return;
      }

      name.textContent = displaySphereName(state.sphere);
      coordinates.textContent =
        `X ${Math.floor(state.x)}   Z ${Math.floor(state.z)}   Y ${Math.floor(state.y)}`;
      compass.setHeading(state.heading);
    },
  };
}

function displaySphereName(id: string): string {
  const local = id.split(":").at(-1)?.split("/").at(-1) ?? id;
  return local
    .split(/[_-]+/)
    .filter(Boolean)
    .map((word) => word[0]?.toUpperCase() + word.slice(1))
    .join(" ");
}
