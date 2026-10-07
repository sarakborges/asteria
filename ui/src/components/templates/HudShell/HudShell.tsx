import type { ReactNode } from "react";
import "./HudShell.css";

export type HudShellProps = {
  crosshair: ReactNode;
  interactionPrompt: ReactNode;
  worldBanner: ReactNode;
  hotbar: ReactNode;
  playerVitals: ReactNode;
  statusEffects: ReactNode;
  toastStack: ReactNode;
  debugOverlay: ReactNode;
  debugVisible: boolean;
};

export function HudShell({
  crosshair,
  interactionPrompt,
  worldBanner,
  hotbar,
  playerVitals,
  statusEffects,
  toastStack,
  debugOverlay,
  debugVisible,
}: HudShellProps) {
  return (
    <main className="hud-shell">
      <div className="hud-shell__center">
        {crosshair}
        {interactionPrompt}
      </div>
      <div className="hud-shell__world">
        {worldBanner}
      </div>
      <div className="hud-shell__bottom-center">
        {hotbar}
      </div>
      <div className="hud-shell__bottom-left">
        {playerVitals}
      </div>
      <div className="hud-shell__top-right">
        {statusEffects}
      </div>
      <div className="hud-shell__toast-area">
        {toastStack}
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
