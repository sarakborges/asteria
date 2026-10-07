import "./LoadingOverlay.css";
import { createText } from "../atoms/Text";

export type LoadingOverlayState = {
  phaseLabel: string;
  completed: number;
  total: number;
  dimension: string;
};

export type LoadingOverlayView = {
  element: HTMLElement;
  show(state: LoadingOverlayState): void;
  hide(): void;
};

export function createLoadingOverlay(): LoadingOverlayView {
  const root = document.createElement("section");
  root.className = "loading-overlay";
  root.hidden = true;
  root.setAttribute("aria-live", "polite");
  root.setAttribute("aria-busy", "true");

  const card = document.createElement("div");
  card.className = "loading-overlay__card";

  const eyebrow = createText({
    text: "ASTERIA / LOADING",
    variant: "eyebrow",
  });
  const title = document.createElement("h1");
  title.className = "loading-overlay__title";
  title.textContent = "Preparando Sphere";

  const phase = createText({
    text: "",
    variant: "detail",
  });
  phase.classList.add("loading-overlay__phase");

  const progress = document.createElement("progress");
  progress.className = "loading-overlay__progress";

  const counters = createText({
    text: "",
    variant: "detail",
  });
  counters.classList.add("loading-overlay__counters");

  const dimension = createText({
    text: "",
    variant: "detail",
  });
  dimension.classList.add("loading-overlay__dimension");

  card.append(
    eyebrow,
    title,
    phase,
    progress,
    counters,
    dimension,
  );
  root.append(card);

  return {
    element: root,
    show(state) {
      root.hidden = false;
      root.setAttribute("aria-busy", "true");
      phase.textContent = state.phaseLabel;
      dimension.textContent = state.dimension;

      if (state.total > 0) {
        progress.max = state.total;
        progress.value = Math.min(
          state.total,
          Math.max(0, state.completed),
        );
        counters.textContent =
          `${Math.max(0, state.completed)} / ${state.total}`;
      } else {
        progress.removeAttribute("value");
        counters.textContent = "";
      }
    },
    hide() {
      root.hidden = true;
      root.setAttribute("aria-busy", "false");
    },
  };
}
