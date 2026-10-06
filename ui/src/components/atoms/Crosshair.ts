import "./Crosshair.css";

export function createCrosshair(): HTMLDivElement {
  const crosshair = document.createElement("div");
  crosshair.className = "crosshair";
  crosshair.setAttribute("aria-hidden", "true");
  return crosshair;
}
