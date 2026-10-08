import {
  useEffect,
  useState,
} from "react";
import type { ToastState } from "../../../state/uiState";
import "./ToastStack.css";

const DEFAULT_DURATION_MS = 3200;
const LEAVE_DURATION_MS = 140;

export type ToastStackProps = {
  toasts: readonly ToastState[];
  onDismiss(id: number): void;
};

export function ToastStack({
  toasts,
  onDismiss,
}: ToastStackProps) {
  return (
    <aside
      className="toast-stack"
      aria-live="polite"
    >
      {toasts.map((toast) => (
        <Toast
          key={toast.id}
          toast={toast}
          onDismiss={onDismiss}
        />
      ))}
    </aside>
  );
}

type ToastProps = {
  toast: ToastState;
  onDismiss(id: number): void;
};

function Toast({
  toast,
  onDismiss,
}: ToastProps) {
  const [leaving, setLeaving] = useState(false);

  useEffect(() => {
    const duration = normalizeDuration(toast.durationMs);
    if (duration === 0) return;

    let leaveTimer = 0;
    const timer = window.setTimeout(() => {
      setLeaving(true);
      leaveTimer = window.setTimeout(
        () => onDismiss(toast.id),
        LEAVE_DURATION_MS,
      );
    }, duration);

    return () => {
      window.clearTimeout(timer);
      if (leaveTimer) {
        window.clearTimeout(leaveTimer);
      }
    };
  }, [
    toast.id,
    toast.durationMs,
    onDismiss,
  ]);

  return (
    <div
      className={[
        "toast-stack__toast",
        "toast-stack__toast--" + toast.tone,
        leaving
          ? "toast-stack__toast--leaving"
          : "",
      ]
        .filter(Boolean)
        .join(" ")}
    >
      {toast.message}
    </div>
  );
}

function normalizeDuration(
  value: number | undefined,
): number {
  if (value === 0) return 0;
  if (!Number.isFinite(value)) {
    return DEFAULT_DURATION_MS;
  }

  return Math.min(
    10_000,
    Math.max(
      800,
      value ?? DEFAULT_DURATION_MS,
    ),
  );
}
