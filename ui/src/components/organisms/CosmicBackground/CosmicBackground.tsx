import type { CSSProperties } from "react";
import "./CosmicBackground.css";

type StarSpec = {
  left: number;
  top: number;
  size: number;
  phase: number;
  speed: number;
  color: [number, number, number];
  alpha: number;
};

const stars: readonly StarSpec[] = [
  { left: 7, top: 12, size: 2.4, phase: 0.1, speed: 0.38, color: [224, 232, 255], alpha: 0.62 },
  { left: 15, top: 73, size: 2.8, phase: 1.7, speed: 0.31, color: [184, 156, 255], alpha: 0.54 },
  { left: 22, top: 28, size: 2.0, phase: 2.4, speed: 0.44, color: [171, 227, 255], alpha: 0.58 },
  { left: 29, top: 88, size: 2.2, phase: 0.7, speed: 0.35, color: [245, 235, 255], alpha: 0.50 },
  { left: 34, top: 16, size: 1.8, phase: 3.2, speed: 0.41, color: [184, 163, 255], alpha: 0.47 },
  { left: 41, top: 68, size: 2.7, phase: 4.1, speed: 0.30, color: [230, 242, 255], alpha: 0.60 },
  { left: 47, top: 9, size: 2.0, phase: 1.1, speed: 0.43, color: [163, 219, 255], alpha: 0.51 },
  { left: 53, top: 82, size: 2.4, phase: 2.9, speed: 0.33, color: [209, 168, 255], alpha: 0.53 },
  { left: 59, top: 21, size: 1.9, phase: 0.5, speed: 0.39, color: [247, 245, 255], alpha: 0.56 },
  { left: 66, top: 61, size: 3.0, phase: 3.8, speed: 0.28, color: [148, 209, 255], alpha: 0.64 },
  { left: 72, top: 34, size: 2.1, phase: 5.0, speed: 0.36, color: [194, 156, 255], alpha: 0.52 },
  { left: 79, top: 84, size: 1.9, phase: 1.9, speed: 0.42, color: [232, 240, 255], alpha: 0.55 },
  { left: 85, top: 18, size: 2.8, phase: 4.6, speed: 0.32, color: [158, 219, 255], alpha: 0.61 },
  { left: 91, top: 69, size: 2.2, phase: 2.2, speed: 0.40, color: [212, 163, 255], alpha: 0.53 },
  { left: 95, top: 39, size: 1.8, phase: 3.4, speed: 0.34, color: [235, 242, 255], alpha: 0.48 },
  { left: 11, top: 47, size: 1.9, phase: 5.4, speed: 0.29, color: [150, 212, 255], alpha: 0.49 },
  { left: 25, top: 55, size: 2.3, phase: 1.3, speed: 0.37, color: [237, 240, 255], alpha: 0.51 },
  { left: 76, top: 11, size: 2.0, phase: 2.7, speed: 0.34, color: [189, 156, 255], alpha: 0.50 }
];

export function CosmicBackground() {
  return (
    <div
      className="cosmic-background"
      aria-hidden="true"
    >
      {stars.map((star, index) => {
        const [red, green, blue] = star.color;
        const style: CSSProperties = {
          left: star.left + "%",
          top: star.top + "%",
          width: star.size + "px",
          height: star.size + "px",
          backgroundColor:
            "rgb(" +
            red +
            " " +
            green +
            " " +
            blue +
            " / " +
            star.alpha +
            ")",
          boxShadow:
            "0 0 7px rgb(" +
            red +
            " " +
            green +
            " " +
            blue +
            " / " +
            star.alpha * 0.55 +
            ")",
          animationDuration:
            3.2 / star.speed + "s",
          animationDelay:
            -star.phase + "s",
        };

        return (
          <span
            key={index}
            className="cosmic-background__star"
            style={style}
          />
        );
      })}
    </div>
  );
}
