import type { ReactNode } from "react";
import "./Surface.css";

export type SurfaceVariant =
  | "frosted"
  | "hud"
  | "inset"
  | "elevated";

export type SurfaceProps = {
  children: ReactNode;
  variant?: SurfaceVariant;
  className?: string;
};

export function Surface({
  children,
  variant = "frosted",
  className,
}: SurfaceProps) {
  return (
    <div
      className={[
        "ui-surface",
        "ui-surface--" + variant,
        className,
      ]
        .filter(Boolean)
        .join(" ")}
    >
      {children}
    </div>
  );
}
