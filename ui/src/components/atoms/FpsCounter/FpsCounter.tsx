import "./FpsCounter.css";

export type FpsCounterProps = {
  fps: number | null;
};

export function FpsCounter({
  fps,
}: FpsCounterProps) {
  if (fps === null) return null;

  return (
    <span className="fps-counter">
      {fps} FPS
    </span>
  );
}
