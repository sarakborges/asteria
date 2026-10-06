import "./HudShell.css";

export type HudShellProps = {
  statusCard: HTMLElement;
  crosshair: HTMLElement;
};

export function createHudShell(props: HudShellProps): HTMLElement {
  const shell = document.createElement("main");
  shell.className = "hud-shell";

  const statusSlot = document.createElement("div");
  statusSlot.className = "hud-shell__status";
  statusSlot.append(props.statusCard);

  shell.append(statusSlot, props.crosshair);
  return shell;
}
