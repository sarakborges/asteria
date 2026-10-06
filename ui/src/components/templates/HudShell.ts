import "./HudShell.css";

export type HudShellProps = {
  crosshair: HTMLElement;
  interactionPrompt: HTMLElement;
  hotbar: HTMLElement;
  playerVitals: HTMLElement;
  statusEffects: HTMLElement;
  toastStack: HTMLElement;
  debugOverlay: HTMLElement;
};

export type HudShellView = {
  element: HTMLElement;
  setDebugVisible(visible: boolean): void;
};

export function createHudShell(props: HudShellProps): HudShellView {
  const shell = document.createElement("main");
  shell.className = "hud-shell";

  const center = slot("hud-shell__center");
  center.append(props.crosshair, props.interactionPrompt);

  const bottomCenter = slot("hud-shell__bottom-center");
  bottomCenter.append(props.hotbar);

  const bottomLeft = slot("hud-shell__bottom-left");
  bottomLeft.append(props.playerVitals);

  const topRight = slot("hud-shell__top-right");
  topRight.append(props.statusEffects);

  const toastArea = slot("hud-shell__toast-area");
  toastArea.append(props.toastStack);

  const debug = slot("hud-shell__debug");
  debug.hidden = true;
  debug.append(props.debugOverlay);

  shell.append(
    center,
    bottomCenter,
    bottomLeft,
    topRight,
    toastArea,
    debug,
  );

  return {
    element: shell,
    setDebugVisible(visible) {
      debug.hidden = !visible;
    },
  };
}

function slot(className: string): HTMLDivElement {
  const element = document.createElement("div");
  element.className = className;
  return element;
}
