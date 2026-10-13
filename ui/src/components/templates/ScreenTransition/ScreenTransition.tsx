import { useLayoutEffect, useRef, useState, type ReactNode } from "react";
import "./ScreenTransition.css";

const HALF_DURATION_MS = 160;
type Phase = "idle" | "out" | "in";

export type ScreenTransitionProps = {
  screenKey: string;
  children: ReactNode;
};

/**
 * UI-only counterpart to MineClone's two 160 ms transition halves. The
 * controller/store owns navigation; this component freezes the outgoing
 * tree until the opaque veil, then reveals the latest requested screen.
 * Rapid navigation is coalesced during fade-out, never swapping stale UI.
 */
export function ScreenTransition({ screenKey, children }: ScreenTransitionProps) {
  const [visibleKey, setVisibleKey] = useState(screenKey);
  const [phase, setPhase] = useState<Phase>("idle");
  const phaseRef = useRef<Phase>("idle");
  const lastVisibleChildren = useRef(children);
  const latest = useRef({ key: screenKey, children });
  const swapTimer = useRef<number | null>(null);
  const finishTimer = useRef<number | null>(null);
  latest.current = { key: screenKey, children };

  if (visibleKey === screenKey && phase === "idle")
    lastVisibleChildren.current = children;

  const setTransitionPhase = (value: Phase) => {
    phaseRef.current = value;
    setPhase(value);
  };

  const clearTimers = () => {
    if (swapTimer.current !== null) window.clearTimeout(swapTimer.current);
    if (finishTimer.current !== null) window.clearTimeout(finishTimer.current);
    swapTimer.current = null;
    finishTimer.current = null;
  };

  const finishFade = () => {
    finishTimer.current = window.setTimeout(() => {
      setTransitionPhase("idle");
      finishTimer.current = null;
    }, HALF_DURATION_MS);
  };

  useLayoutEffect(() => {
    if (screenKey === visibleKey) {
      // A pending navigation was reversed before reaching black.
      if (phaseRef.current === "out") {
        clearTimers();
        setTransitionPhase("in");
        finishFade();
      }
      return;
    }
    if (window.matchMedia?.("(prefers-reduced-motion: reduce)")?.matches) {
      clearTimers();
      lastVisibleChildren.current = latest.current.children;
      setVisibleKey(latest.current.key);
      setTransitionPhase("idle");
      return;
    }

    // While fading out, a new navigation target supersedes the old target
    // without restarting the clock or exposing an intermediate screen.
    if (phaseRef.current === "out") return;
    clearTimers();
    setTransitionPhase("out");
    swapTimer.current = window.setTimeout(() => {
      swapTimer.current = null;
      const target = latest.current;
      lastVisibleChildren.current = target.children;
      setVisibleKey(target.key);
      setTransitionPhase("in");
      finishFade();
    }, HALF_DURATION_MS);
    // Key changes are authoritative. Children changes within one page do not
    // cancel the transition or reset its duration.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [screenKey]);

  useLayoutEffect(() => () => clearTimers(), []);

  return (
    <div className={"screen-transition screen-transition--" + phase}
      aria-busy={phase !== "idle"}>
      {visibleKey === screenKey ? children : lastVisibleChildren.current}
      <div className={"screen-transition__veil screen-transition__veil--" + phase}
        aria-hidden="true" />
    </div>
  );
}
