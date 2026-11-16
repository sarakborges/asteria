import "./Compass.css";

const PIXELS_PER_DEGREE = 2;
const TRACK_ORIGIN_DEGREES = 360;
const MARKER_STEP_DEGREES = 15;
const TRACK_MIN_DEGREES = -360;
const TRACK_MAX_DEGREES = 720;

const markers = Array.from(
  {
    length:
      (TRACK_MAX_DEGREES - TRACK_MIN_DEGREES) /
        MARKER_STEP_DEGREES +
      1,
  },
  (_, index) =>
    TRACK_MIN_DEGREES +
    index * MARKER_STEP_DEGREES,
);

export type CompassProps = {
  heading: number;
};

export function Compass({
  heading,
}: CompassProps) {
  const normalized = normalizeDegrees(
    Number.isFinite(heading)
      ? heading
      : 0,
  );
  const offset =
    -(normalized + TRACK_ORIGIN_DEGREES) *
    PIXELS_PER_DEGREE;

  return (
    <div
      className="compass"
      aria-label="Compass"
      data-heading={normalized.toFixed(1)}
    >
      <div className="compass__viewport">
        <div
          className="compass__track"
          style={{
            transform:
              "translateX(" +
              offset +
              "px)",
          }}
        >
          {markers.map((angle) => {
            const markerAngle =
              normalizeDegrees(angle);
            const label =
              directionLabel(markerAngle);

            return (
              <div
                key={angle}
                className={
                  label
                    ? "compass__marker compass__marker--major"
                    : "compass__marker"
                }
                style={{
                  left:
                    (angle +
                      TRACK_ORIGIN_DEGREES) *
                      PIXELS_PER_DEGREE +
                    "px",
                }}
              >
                <span className="compass__tick" />
                {label && (
                  <span className="compass__label">
                    {label}
                  </span>
                )}
              </div>
            );
          })}
        </div>
        <div className="compass__center" />
      </div>
    </div>
  );
}

function directionLabel(
  angle: number,
): string {
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

function normalizeDegrees(
  value: number,
): number {
  const wrapped = value % 360;
  return wrapped < 0
    ? wrapped + 360
    : wrapped;
}
