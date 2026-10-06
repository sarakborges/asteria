import "./Compass.css";

const PIXELS_PER_DEGREE = 2;
const TRACK_ORIGIN_DEGREES = 360;
const MARKER_STEP_DEGREES = 15;
const TRACK_MIN_DEGREES = -360;
const TRACK_MAX_DEGREES = 720;

export type CompassView = {
  element: HTMLElement;
  setHeading(headingDegrees: number): void;
};

export function createCompass(): CompassView {
  const root = document.createElement("div");
  root.className = "compass";
  root.setAttribute("aria-label", "Compass");

  const viewport = document.createElement("div");
  viewport.className = "compass__viewport";

  const track = document.createElement("div");
  track.className = "compass__track";

  for (
    let angle = TRACK_MIN_DEGREES;
    angle <= TRACK_MAX_DEGREES;
    angle += MARKER_STEP_DEGREES
  ) {
    const marker = document.createElement("div");
    const normalized = normalizeDegrees(angle);
    const label = directionLabel(normalized);

    marker.className = label
      ? "compass__marker compass__marker--major"
      : "compass__marker";
    marker.style.left = `${(angle + TRACK_ORIGIN_DEGREES) * PIXELS_PER_DEGREE}px`;

    const tick = document.createElement("span");
    tick.className = "compass__tick";
    marker.append(tick);

    if (label) {
      const text = document.createElement("span");
      text.className = "compass__label";
      text.textContent = label;
      marker.append(text);
    }

    track.append(marker);
  }

  const center = document.createElement("div");
  center.className = "compass__center";

  viewport.append(track, center);
  root.append(viewport);

  return {
    element: root,
    setHeading(headingDegrees) {
      const heading = normalizeDegrees(
        Number.isFinite(headingDegrees) ? headingDegrees : 0,
      );
      const offset =
        -(heading + TRACK_ORIGIN_DEGREES) * PIXELS_PER_DEGREE;
      track.style.transform =
        `translateX(${offset}px)`;
      root.dataset.heading = heading.toFixed(1);
    },
  };
}

function directionLabel(angle: number): string {
  switch (angle) {
    case 0:
      return "N";
    case 45:
      return "NE";
    case 90:
      return "E";
    case 135:
      return "SE";
    case 180:
      return "S";
    case 225:
      return "SW";
    case 270:
      return "W";
    case 315:
      return "NW";
    default:
      return "";
  }
}

function normalizeDegrees(value: number): number {
  const wrapped = value % 360;
  return wrapped < 0 ? wrapped + 360 : wrapped;
}
