import "./StatusEffects.css";

export type StatusEffectTone = "positive" | "negative" | "neutral";

export type StatusEffectState = {
  id: string;
  label: string;
  duration?: string;
  tone?: StatusEffectTone;
};

export type StatusEffectsView = {
  element: HTMLElement;
  setEffects(effects: readonly StatusEffectState[]): void;
};

export function createStatusEffects(): StatusEffectsView {
  const root = document.createElement("aside");
  root.className = "status-effects";
  root.hidden = true;

  return {
    element: root,
    setEffects(effects) {
      root.replaceChildren();
      root.hidden = effects.length === 0;

      for (const effect of effects.slice(0, 8)) {
        const item = document.createElement("div");
        item.className =
          `status-effects__item status-effects__item--${effect.tone ?? "neutral"}`;
        item.dataset.effectId = effect.id;

        const marker = document.createElement("span");
        marker.className = "status-effects__marker";
        marker.setAttribute("aria-hidden", "true");

        const copy = document.createElement("span");
        copy.className = "status-effects__copy";

        const label = document.createElement("strong");
        label.textContent = effect.label;

        const duration = document.createElement("span");
        duration.className = "status-effects__duration";
        duration.textContent = effect.duration ?? "";

        copy.append(label, duration);
        item.append(marker, copy);
        root.append(item);
      }
    },
  };
}
