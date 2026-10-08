import { useEffect, useRef, useState, type ReactNode } from "react";
import "./ScreenTransition.css";

const HALF_DURATION_MS = 160;

type Phase = "idle" | "out" | "in";

export type ScreenTransitionProps = {
  screenKey: string;
  children: ReactNode;
};

/**
 * MineClone-style screen fade for UI-only navigation.
 * The navigation/controller/store is authoritative; this component only keeps
 * the outgoing presentation on screen until the fade reaches black.
 */
export function ScreenTransition({ screenKey, children }: ScreenTransitionProps) {
  const [visibleKey, setVisibleKey] = useState(screenKey);
  const [phase, setPhase] = useState<Phase>("idle");
  const lastVisibleChildren = useRef(children);

  // Keep the outgoing tree only while switching screen identity.
  if (visibleKey === screenKey && phase === "idle") {
    lastVisibleChildren.current = children;
  }

  useEffect(() => {
    if (screenKey === visibleKey) return;
    if (window.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches) {
      setVisibleKey(screenKey);
      setPhase("idle");
      return;
    }

    setPhase("out");
    const swap = window.setTimeout(() => {
      setVisibleKey(screenKey);
      setPhase("in");
    }, HALF_DURATION_MS);
    const finish = window.setTimeout(() => setPhase("idle"), HALF_DURATION_MS * 2);
    return () => {
      window.clearTimeout(swap);
      window.clearTimeout(finish);
    };
  }, [screenKey, visibleKey]);

  return (
    <div className={"screen-transition screen-transition--" + phase}
      aria-busy={phase !== "idle"}>
      {visibleKey === screenKey ? children : lastVisibleChildren.current}
      {phase !== "idle" && (
        <div className={"screen-transition__veil screen-transition__veil--" + phase}
          aria-hidden="true" />
      )}
    </div>
  );
}
