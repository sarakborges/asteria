import "./ToastStack.css";

export type ToastTone = "info" | "success" | "warning";

export type ToastMessage = {
  message: string;
  tone?: ToastTone;
  durationMs?: number;
};

export type ToastStackView = {
  element: HTMLElement;
  push(toast: ToastMessage): void;
  clear(): void;
};

const MAX_TOASTS = 4;
const DEFAULT_DURATION_MS = 3200;

export function createToastStack(): ToastStackView {
  const root = document.createElement("aside");
  root.className = "toast-stack";
  root.setAttribute("aria-live", "polite");

  const removeToast = (toast: HTMLElement): void => {
    toast.classList.add("toast-stack__toast--leaving");
    window.setTimeout(() => toast.remove(), 140);
  };

  return {
    element: root,
    push(toast) {
      const item = document.createElement("div");
      item.className =
        `toast-stack__toast toast-stack__toast--${toast.tone ?? "info"}`;
      item.textContent = toast.message;
      root.append(item);

      while (root.children.length > MAX_TOASTS) {
        root.firstElementChild?.remove();
      }

      const duration = normalizeDuration(toast.durationMs);
      if (duration > 0) {
        window.setTimeout(() => {
          if (item.isConnected) removeToast(item);
        }, duration);
      }
    },
    clear() {
      root.replaceChildren();
    },
  };
}

function normalizeDuration(value: number | undefined): number {
  if (value === 0) return 0;
  if (!Number.isFinite(value)) return DEFAULT_DURATION_MS;
  return Math.min(10_000, Math.max(800, value ?? DEFAULT_DURATION_MS));
}
