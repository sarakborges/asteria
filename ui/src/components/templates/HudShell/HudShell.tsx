import type { ReactNode } from "react";
import "./HudShell.css";

export type HudShellProps = {
  crosshair: ReactNode;
  interactionPrompt: ReactNode;
  targetOverlay: ReactNode;
  targetPosition?: "Center" | "TopRight" | "Hidden";
  worldBanner: ReactNode;
  worldClock: ReactNode;
  hotbar: ReactNode;
  playerHud: ReactNode;
  statusEffects: ReactNode;
  toastStack: ReactNode;
  fpsCounter: ReactNode;
  debugOverlay: ReactNode;
  debugVisible: boolean;
};

export function HudShell({
  crosshair,
  interactionPrompt,
  targetOverlay,
  targetPosition = "Center",
  worldBanner,
  worldClock,
  hotbar,
  playerHud,
  statusEffects,
  toastStack,
  fpsCounter,
  debugOverlay,
  debugVisible,
}: HudShellProps) {
  return (
    <main className="hud-shell">
      <div className="hud-shell__center">
        {crosshair}
        {interactionPrompt}
      </div>
      <div className={"hud-shell__target" + (
        targetPosition === "TopRight" ? " hud-shell__target--top-right" : ""
      )}>
        {targetOverlay}
      </div>
      <div className="hud-shell__world">
        {worldBanner}
      </div>
      <div className="hud-shell__clock">
        {worldClock}
      </div>
      <div className="hud-shell__bottom-center">
        {hotbar}
      </div>
      <div className="hud-shell__bottom-left">
        {playerHud}
      </div>
      <div className="hud-shell__top-right">
        {statusEffects}
      </div>
      <div className="hud-shell__toast-area">
        {toastStack}
      </div>
      <div className="hud-shell__fps">
        {fpsCounter}
      </div>
      <div
        className="hud-shell__debug"
        hidden={!debugVisible}
      >
        {debugOverlay}
      </div>
    </main>
  );
}
